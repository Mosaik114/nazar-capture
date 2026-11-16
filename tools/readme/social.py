"""Social preview 1280x640 for Nazar Capture: chalk white, painted mark, cobalt wash, the viewfinder banner (from the app).

The mark is the app's logo; a larger rendering of it can be given as the first argument for a sharper result."""
import os
from pathlib import Path
import random
import sys
from PIL import Image, ImageDraw, ImageFilter, ImageFont

REPO = Path(__file__).resolve().parents[2]
RAW = Path(os.environ["NAZAR_README_OUT"])
MARK = Path(sys.argv[1]) if len(sys.argv) > 1 else REPO / "engine/NazarUi/Logo/nazar_mark.png"

FONT = REPO / "engine/NazarUi/Fonts/Nunito-wght.ttf"
W, H = 1280, 640
WINDOW_BG, TEXT_PRIMARY, TEXT_SECONDARY, TEXT_TERTIARY, ACCENT = "#F5F4F1", "#1C2B45", "#4C5568", "#5A6274", "#315C99"

def font(size, weight):
    f = ImageFont.truetype(str(FONT), size); f.set_variation_by_name(weight); return f

random.seed(3)
def noise(w, h):
    im = Image.new("L", (w, h)); im.putdata([random.randrange(256) for _ in range(w * h)]); return im.resize((W, H), Image.BICUBIC)

canvas = Image.new("RGB", (W, H), WINDOW_BG)
# a faint mottling, as on limewash
canvas = Image.composite(Image.new("RGB", (W, H), "#FBFAF8"), canvas, noise(W // 8, H // 8).point(lambda v: v * 0.35))
canvas = Image.composite(Image.new("RGB", (W, H), "#ECEAE5"), canvas, noise(W // 8, H // 8).point(lambda v: v * 0.30))
# a soft cobalt wash behind the mark
cx, cy = 300, 320
blob = Image.new("L", (W, H), 0)
ImageDraw.Draw(blob).ellipse((cx - 200, cy - 185, cx + 200, cy + 185), fill=255)
blob = blob.filter(ImageFilter.GaussianBlur(70))
blob = Image.composite(blob, Image.new("L", (W, H), 0), noise(W // 20, H // 20).point(lambda v: 140 + v // 2))
blob = blob.point(lambda v: int(v * 0.22)).filter(ImageFilter.GaussianBlur(12))
canvas = Image.composite(Image.new("RGB", (W, H), "#33609F"), canvas, blob).convert("RGBA")
mark = Image.open(MARK).convert("RGBA").resize((300, 300), Image.LANCZOS)
canvas.alpha_composite(mark, (cx - 150, cy - 150))
draw = ImageDraw.Draw(canvas)
x0 = 540
draw.text((x0, 150), "Nazar Capture", font=font(92, "ExtraBold"), fill=TEXT_PRIMARY)
draw.text((x0 + 3, 268), "Keyboard-first screenshots and", font=font(38, "SemiBold"), fill=TEXT_SECONDARY)
draw.text((x0 + 3, 316), "screen recordings for Windows", font=font(38, "SemiBold"), fill=TEXT_SECONDARY)
banner = Image.open(RAW / "banner.png").convert("RGBA")
banner = banner.resize((banner.width // 2, banner.height // 2), Image.LANCZOS)
canvas.alpha_composite(banner, (x0 - 6, 378))
small = font(27, "Bold")
draw.text((x0 + 3, 470), "Editor · offline text recognition · GPU recording", font=small, fill=TEXT_TERTIARY)
draw.text((x0 + 3, 510), "Shift + Alt + 4, drag, and it is on the clipboard", font=small, fill=ACCENT)
out = REPO / "docs/images/social-preview.png"
canvas.convert("RGB").save(out, optimize=True)
print(out, out.stat().st_size // 1024, "KB")
