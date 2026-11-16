"""Logo of Nazar Capture: concept "Fokus" in cobalt, ink and watercolour (refined from the concept sheet).

usage: python nazar_logo.py <out dir>   (numpy, Pillow)
"""
"""Bildzeichen fuer das Capture-Tool (Schwester von Hams) in Tusche und Aquarell.

Erweitert den Hams-Generator (aquarell.py): gleiche Malweise (Tuschekontur, Aquarellkoerper mit
dunklerer Seite, Pigmentrand, weiches Licht, Papierkorn), aber mit mehreren Konzepten, die jeweils
aus runden Pinselstrichen bestehen. Pro Konzept und Farbe entstehen:
  - <konzept>_<farbe>_zeichen.png  (512 px, freigestellt)
  - <konzept>_<farbe>_icon.png     (512 px, App-Icon: Aquarell-Lavur in der Akzentfarbe, Zeichen in Creme)
  - <konzept>_<farbe>_klein.png    (Pruefbogen 16/24/32/48 px, hell und dunkel, 8-fach vergroessert)
  - klein/<konzept>_<farbe>_<groesse>.png (echte Kleingroessen fuer die Vergleichsseite)

usage: python capture_logo.py <out_dir> [konzept ...]
"""
import math
import sys
from pathlib import Path

import numpy as np
from PIL import Image, ImageChops, ImageDraw, ImageFilter

OUT = Path(".")
OUT.mkdir(parents=True, exist_ok=True)
(OUT / "klein").mkdir(exist_ok=True)
ONLY: set = set()
S = 1  # 1024er Arbeitsflaeche; Ausgabe 512 px ist damit zweifach ueberabgetastet
N = 1024 * S


def hexrgb(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) for i in (0, 2, 4)], np.float32)


def cubic(p0, p1, p2, p3, n=400):
    t = np.linspace(0, 1, n)[:, None]
    return ((1 - t) ** 3) * p0 + 3 * ((1 - t) ** 2) * t * p1 + 3 * (1 - t) * t * t * p2 + t ** 3 * p3


def line(a, b, n=None):
    if n is None:
        n = max(2, int(np.linalg.norm(b - a) / 1.5))
    t = np.linspace(0, 1, n)[:, None]
    return (1 - t) * a + t * b


P = lambda x, y: np.array([x, y], np.float32)  # noqa: E731
K = 0.5523  # Kreisnaeherung fuer Bezier-Viertelboegen


def corner(a, c, b):
    """Viertelbogen von a ueber die Ecke c nach b (a-c und c-b stehen senkrecht)."""
    return cubic(a, a + (c - a) * K, b + (c - b) * K, b)


def bracket(cx, cy, dx, dy, arm, rc):
    """L-foermige Ecke mit Scheitel (cx, cy); dx/dy = +-1 geben die Richtung der Schenkel an."""
    c = P(cx, cy)
    a_end, a_in = P(cx, cy + dy * arm), P(cx, cy + dy * rc)
    b_in, b_end = P(cx + dx * rc, cy), P(cx + dx * arm, cy)
    return np.concatenate([line(a_end, a_in), corner(a_in, c, b_in), line(b_in, b_end)])


def rounded_rect(x0, y0, x1, y1, r):
    pts = []
    cs = [(P(x0, y0), P(x0, y0 + r), P(x0 + r, y0)), (P(x1, y0), P(x1 - r, y0), P(x1, y0 + r)),
          (P(x1, y1), P(x1, y1 - r), P(x1 - r, y1)), (P(x0, y1), P(x0 + r, y1), P(x0, y1 - r))]
    for i, (c, a, b) in enumerate(cs):
        pts.append(corner(a, c, b))
        nxt = cs[(i + 1) % 4][1]
        pts.append(line(b, nxt))
    return np.concatenate(pts)


def circle(cx, cy, r, a0=0.0, a1=2 * math.pi, n=700):
    t = np.linspace(a0, a1, n)
    return np.stack([cx + r * np.cos(t), cy + r * np.sin(t)], 1).astype(np.float32)


def dot(cx, cy):
    return np.array([[cx, cy]], np.float32)


# ---------------------------------------------------------------- Konzepte
# Jedes Konzept: Liste von (Punkte, Strichbreite). Ein einzelner Punkt mit Breite w ist ein Kreis.
W = 112  # Grundstrich (Hams: 136 bei weniger Elementen)

CONCEPTS = {}

# A  Sucher: vier Ecken eines Ausschnitts, in der Mitte ein Fokuspunkt
_a = 300
CONCEPTS["sucher"] = dict(
    name="Sucher",
    strokes=[(bracket(512 - _a, 512 - _a, 1, 1, 190, 70), W), (bracket(512 + _a, 512 - _a, -1, 1, 190, 70), W),
             (bracket(512 - _a, 512 + _a, 1, -1, 190, 70), W), (bracket(512 + _a, 512 + _a, -1, -1, 190, 70), W),
             (dot(512, 512), 170)],
    text="Vier Sucherecken mit Fokuspunkt: das bekannteste Bild fuer ‚diesen Ausschnitt festhalten‘, ruhig und symmetrisch.",
)

# B  Linse: ein Objektivring, eingefasst von zwei diagonalen Ecken
CONCEPTS["linse"] = dict(
    name="Linse",
    strokes=[(circle(512, 512, 150), W), (bracket(196, 196, 1, 1, 200, 80), W), (bracket(828, 828, -1, -1, 200, 80), W)],
    text="Ein Objektivring zwischen zwei Ausschnittecken: Linse und Rahmen in einem, mit Bewegung durch die Diagonale.",
)

# C  Zuschnitt: zwei verschraenkte Winkel wie beim Freistellen
CONCEPTS["zuschnitt"] = dict(
    name="Zuschnitt",
    strokes=[(np.concatenate([line(P(372, 176), P(372, 572)), corner(P(372, 572), P(372, 652), P(452, 652)), line(P(452, 652), P(848, 652))]), W),
             (np.concatenate([line(P(176, 372), P(572, 372)), corner(P(572, 372), P(652, 372), P(652, 452)), line(P(652, 452), P(652, 848))]), W)],
    text="Zwei verschraenkte Winkel, das Freistell-Zeichen: steht direkt fuer ‚Ausschnitt waehlen‘ und bleibt klein sehr klar.",
)

# D  Aufnahme: ein Bildschirmrahmen mit Aufnahmepunkt
CONCEPTS["aufnahme"] = dict(
    name="Aufnahme",
    strokes=[(rounded_rect(214, 254, 810, 770, 150), W), (dot(512, 512), 230)],
    text="Ein Bildschirmrahmen mit rundem Aufnahmepunkt: zeigt Bildschirm und Aufnahme, Foto wie Video.",
)

# E  Fadenkreuz: zwei Ecken und das Auswahlkreuz, mit dem man den Bereich aufzieht
CONCEPTS["fadenkreuz"] = dict(
    name="Fadenkreuz",
    strokes=[(bracket(212, 212, 1, 1, 230, 80), W), (bracket(812, 812, -1, -1, 230, 80), W),
             (line(P(512, 340), P(512, 440)), 92), (line(P(512, 584), P(512, 684)), 92),
             (line(P(340, 512), P(440, 512)), 92), (line(P(584, 512), P(684, 512)), 92)],
    text="Das Fadenkreuz, mit dem man den Bereich aufzieht, zwischen Start- und Endecke: erzaehlt den Vorgang selbst.",
)

# F  Fokus (verfeinert fuer Nazar Capture): ein Strich als Objektivring, fast ein Kreis. Die Oeffnung liegt oben
# rechts, gegenueber der einen eckigen Ecke unten links; die Ecke ist klein und ragt kaum ueber den Kreis. So liest
# sich das Zeichen weder als "D" (eckige Seite gegen runde Seite) noch als Tropfen (eine Spitze am geschlossenen Ring).
_r = 282
_c = P(512, 512)


def _on_ring(deg):
    a = math.radians(deg)
    return P(512 + _r * math.cos(a), 512 + _r * math.sin(a))


_a, _b = _on_ring(118), _on_ring(152)          # where the ring leaves for the corner and comes back
_tip = P(_b[0], _a[1])                          # the square corner between them
_round = 16                                     # a corner of ink, not of a machine
_fokus = np.concatenate([
    circle(512, 512, _r, math.radians(-42), math.radians(118), 700),
    line(_a, _tip + P(_round, 0)),
    corner(_tip + P(_round, 0), _tip, _tip - P(0, _round)),
    line(_tip - P(0, _round), _b),
    circle(512, 512, _r, math.radians(152), math.radians(278), 520),
])
CONCEPTS["fokus"] = dict(
    name="Fokus",
    strokes=[(_fokus, W), (dot(512, 512), 168)],
    text="Ein Objektivring aus einem Strich mit einer kleinen eckigen Ecke (Ausschnitt) und dem Fokuspunkt.",
)


def smooth_noise(n, scale, seed):
    r = np.random.default_rng(seed)
    knots = r.uniform(-1, 1, int(n / scale) + 3)
    x = np.arange(n) / scale
    i = np.floor(x).astype(int)
    f = x - i
    f = f * f * (3 - 2 * f)
    return knots[i] * (1 - f) + knots[i + 1] * f


def stroke(draw, pts, width, wobble, seed, shift=(0, 0), grow=0.0):
    wob = smooth_noise(len(pts), 40, seed) * wobble
    for (x, y), w in zip(pts, wob):
        r = (width / 2 * (1 + w) + grow) * S
        cx, cy = (x + shift[0]) * S, (y + shift[1]) * S
        draw.ellipse((cx - r, cy - r, cx + r, cy + r), fill=255)


def make_shape(strokes):
    ref_w = max(w for _, w in strokes if True)

    def shape(scale=1.0, offset=(0, 0), grow=0.0, center=(512, 512), depth=0.0):
        """Das Zeichen als Maske. `depth` schiebt jeden Strich proportional zu seiner Breite nach unten."""
        im = Image.new("L", (N, N), 0)
        d = ImageDraw.Draw(im)
        cx, cy = center
        for k, (pts, w) in enumerate(strokes):
            q = (pts - [cx, cy]) * scale + [cx, cy]
            drop = depth * min(1.0, w / W)
            thin = 0.7 if w < W * 0.9 else 1.0
            stroke(d, q, w * scale, WOBBLE, k + 1, (offset[0], offset[1] + drop), grow * thin)
        return im
    return shape


def field_noise(freq, seed, blur):
    r = np.random.default_rng(seed)
    small = Image.fromarray((r.random((freq, freq)) * 255).astype(np.uint8))
    big = small.resize((N, N), Image.BICUBIC).filter(ImageFilter.GaussianBlur(blur))
    return np.asarray(big, np.float32) / 255


def grain(seed, amount):
    r = np.random.default_rng(seed)
    g = r.normal(0, 1, (N // 2, N // 2)).astype(np.float32)
    g = np.asarray(Image.fromarray(((g * 40) + 128).clip(0, 255).astype(np.uint8)).resize((N, N), Image.BILINEAR), np.float32)
    return (g - 128) / 128 * amount


def arr(im):
    return np.asarray(im, np.float32) / 255


SIDE = 14
GROW = 9
WOBBLE = 0.018      # the ink line breathes a little, like a brush
GLOSS = 0.14        # the old 0.55 read as plastic
PIGMENT_EDGE = 0.34  # watercolour gathers at the edge of a wash
GRANULATION = 0.045


def centred_shift(shape, scale):
    foot = ImageChops.lighter(shape(scale, (0, 0), grow=GROW), shape(scale, (0, 0), grow=GROW, depth=SIDE))
    x0, y0, x1, y1 = foot.getbbox()
    return ((N - (x0 + x1)) / 2 / S, (N - (y0 + y1)) / 2 / S)


def footprint(shape, scale, shift):
    return ImageChops.lighter(shape(scale, shift, grow=GROW), shape(scale, shift, grow=GROW, depth=SIDE))


def paint_mark(canvas, shape, body_cols, scale, ink="#2E2B3F", side="#0A6A61", shadow_amt=0.20):
    body_shift = centred_shift(shape, scale)
    body = shape(scale, body_shift)
    sidem = shape(scale, body_shift, depth=SIDE)
    foot = footprint(shape, scale, body_shift)
    ink_a = arr(foot.filter(ImageFilter.GaussianBlur(0.9 * S)))
    side_a = arr(sidem.filter(ImageFilter.GaussianBlur(0.5 * S)))
    body_a = arr(body.filter(ImageFilter.GaussianBlur(0.5 * S)))

    out = canvas.copy()
    shadow = arr(foot.filter(ImageFilter.GaussianBlur(18 * S)))
    out *= (1 - shadow_amt * np.roll(shadow, 14 * S, axis=0))[..., None]
    out = out * (1 - ink_a[..., None]) + hexrgb(ink) * ink_a[..., None]
    out = out * (1 - side_a[..., None]) + hexrgb(side) * side_a[..., None]

    yy, xx = np.mgrid[0:N, 0:N].astype(np.float32) / N
    t = np.clip((xx * 0.55 + yy * 0.75) - 0.25, 0, 1)
    light, deep = hexrgb(body_cols[0]), hexrgb(body_cols[1])
    col = light[None, None] * (1 - t[..., None]) + deep[None, None] * t[..., None]
    blot = field_noise(9, 5, 30 * S) - 0.5
    col *= (1 + 0.16 * blot)[..., None]
    inner = arr(body.filter(ImageFilter.GaussianBlur(8 * S)))
    edge = np.clip((body_a - inner) * 2.2, 0, 1)
    col *= (1 - PIGMENT_EDGE * edge)[..., None]
    gran = field_noise(70, 13, 1.2 * S) - 0.5
    col *= (1 + GRANULATION * gran)[..., None]
    hi = arr(shape(scale * 0.985, (body_shift[0] - 8, body_shift[1] - 10)).filter(ImageFilter.GaussianBlur(10 * S)))
    hi = np.clip(hi - arr(shape(scale * 0.985, (body_shift[0] + 4, body_shift[1] + 6)).filter(ImageFilter.GaussianBlur(10 * S))), 0, 1)
    col = col + (255 - col) * (GLOSS * hi)[..., None]
    out = out * (1 - body_a[..., None]) + col * body_a[..., None]
    return out, body_shift


def paper(base, wash=None, seed=3):
    c = np.empty((N, N, 3), np.float32)
    c[:] = hexrgb(base)
    fib = field_noise(40, seed, 3 * S) - 0.5
    c *= (1 + 0.035 * fib)[..., None]
    if wash is not None:
        col, strength = wash
        w = field_noise(5, seed + 7, 60 * S)
        c = c * (1 - strength * w[..., None]) + hexrgb(col) * (strength * w[..., None])
    return c


def finish(c, rounded=True, alpha=None, grain_amt=7.0, size=512):
    c = c + grain(9, grain_amt)[..., None]
    im = Image.fromarray(np.clip(c, 0, 255).astype(np.uint8), "RGB")
    if rounded:
        m = Image.new("L", (N, N), 0)
        ImageDraw.Draw(m).rounded_rectangle((0, 0, N - 1, N - 1), int(N * 0.225), fill=255)
        im.putalpha(m)
    elif alpha is not None:
        im.putalpha(alpha)
    return im.resize((size, size), Image.LANCZOS)


# ---------------------------------------------------------------- Farben
# (hell, tief) fuer den Koerper, Seite (Steinkante), Lavur-Grund fuer das App-Icon (Grund, Wolke)
PALETTES = {
    "kobalt": dict(body=("#6A9BD8", "#33609F"), side="#22426F", wash=("#4A79B8", "#7BA3D4"), cream_side="#B8BFCB",
                   label="Kobalt (Vorschlag)", hexes="#6A9BD8 / #33609F"),
    "tuerkis": dict(body=("#2CC4A8", "#0B8579"), side="#0A6A61", wash=("#2AAE98", "#5CC9B3"), cream_side="#C9B79A",
                    label="Türkis (wie Hams)", hexes="#2CC4A8 / #0B8579"),
}

MARK_FILL = 0.94  # Anteil der Flaeche, den das freigestellte Zeichen einnimmt
ICON_FILL = 0.64  # Anteil im App-Icon (Hams: etwa 0,62)


def fit_scale(shape, fill):
    """Skalierung, bei der der gemalte Fussabdruck (Kontur und Seite) `fill` der Flaeche einnimmt."""
    x0, y0, x1, y1 = footprint(shape, 1.0, (0, 0)).getbbox()
    return fill * N / max(x1 - x0, y1 - y0)


def small_sheet(zeichen, icon, key):
    """Pruefbogen: Zeichen und Icon in 16/24/32/48 px auf hell und dunkel, 8-fach vergroessert (nearest)."""
    sizes = (16, 24, 32, 48)
    grounds = (("hell", (247, 243, 234)), ("weiss", (255, 255, 255)), ("dunkel", (32, 32, 36)), ("schwarz", (12, 12, 14)))
    Z = 6
    cell = 48 * Z + 16
    sheet = Image.new("RGB", (len(sizes) * 2 * cell + 16, len(grounds) * cell + 16), (128, 128, 128))
    for gi, (gname, gcol) in enumerate(grounds):
        for si, s in enumerate(sizes):
            for vi, src in enumerate((zeichen, icon)):
                small = src.resize((s, s), Image.LANCZOS)
                if gi == 0:
                    small.save(OUT / "klein" / f"{key}_{'zeichen' if vi == 0 else 'icon'}_{s}.png")
                tile = Image.new("RGBA", (s, s), gcol + (255,))
                tile.alpha_composite(small)
                big = tile.convert("RGB").resize((s * Z, s * Z), Image.NEAREST)
                x = 16 + (vi * len(sizes) + si) * cell
                y = 16 + gi * cell
                bg = Image.new("RGB", (cell - 16, cell - 16), gcol)
                bg.paste(big, ((cell - 16 - s * Z) // 2, (cell - 16 - s * Z) // 2))
                sheet.paste(bg, (x, y))
    return sheet


def render(key, concept, pkey, pal):
    shape = make_shape(concept["strokes"])
    # freigestelltes Zeichen in der Akzentfarbe
    base = paper("#F7F1E3")
    ms = fit_scale(shape, MARK_FILL)
    mark, shift = paint_mark(base, shape, pal["body"], ms, side=pal["side"], shadow_amt=0.0)
    alpha = footprint(shape, ms, shift).filter(ImageFilter.GaussianBlur(0.6 * S))
    zeichen = finish(mark, rounded=False, alpha=alpha, grain_amt=4)
    zeichen.save(OUT / f"{key}_{pkey}_zeichen.png")

    # App-Icon: Aquarell-Lavur in der Akzentfarbe, Zeichen in Creme (wie das Hams-App-Icon)
    bg = paper(pal["wash"][0], (pal["wash"][1], 0.5), seed=21)
    yy, xx = np.mgrid[0:N, 0:N].astype(np.float32) / N
    bg *= (1.08 - 0.25 * (yy * 0.7 + xx * 0.3))[..., None]
    ic, _ = paint_mark(bg, shape, ("#FFFDF7", "#EFE3C8"), fit_scale(shape, ICON_FILL), side=pal["cream_side"])
    icon = finish(ic)
    icon.save(OUT / f"{key}_{pkey}_icon.png")

    small_sheet(zeichen, icon, f"{key}_{pkey}").save(OUT / f"{key}_{pkey}_klein.png")
    return zeichen, icon


def simple_icon(size, pal):
    """16 and 20 px: the same wash and mark, but a thicker line, a larger point, no paper and no grain."""
    strokes = [(CONCEPTS["fokus"]["strokes"][0][0], W * 1.55), (dot(512, 512), 230)]
    shape = make_shape(strokes)
    bg = np.empty((N, N, 3), np.float32)
    bg[:] = hexrgb(pal["body"][1])
    ic, _ = paint_mark(bg, shape, ("#FFFDF7", "#F4EAD3"), fit_scale(shape, 0.80), side=pal["cream_side"], shadow_amt=0.0)
    return finish(ic, grain_amt=0.0, size=size)


def build(out):
    """Writes zeichen/icon PNGs, the ICO (16, 20, 24, 32, 48, 256) and the check sheet into `out`."""
    global OUT
    OUT = Path(out)
    (OUT / "klein").mkdir(parents=True, exist_ok=True)
    pal = PALETTES["kobalt"]
    zeichen, icon = render("fokus", CONCEPTS["fokus"], "kobalt", pal)
    big = icon.resize((256, 256), Image.LANCZOS)
    small = {s: (simple_icon(s, pal) if s <= 20 else icon.resize((s, s), Image.LANCZOS)) for s in (16, 20, 24, 32, 48)}
    for s, im in small.items():
        im.save(OUT / f"nazar_icon_{s}.png")
    big.save(OUT / "nazar_icon_256.png")
    big.save(OUT / "nazar.ico", sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (48, 48), (256, 256)],
             append_images=[small[16], small[20], small[24], small[32], small[48]])
    zeichen.resize((256, 256), Image.LANCZOS).save(OUT / "nazar_mark_256.png")
    return zeichen, icon


if __name__ == "__main__":
    build(sys.argv[1] if len(sys.argv) > 1 else "out")
    print("ok", flush=True)
