"""Draws the VoiceWeave app icon and writes desktop/VoiceWeave.App/Assets/app.ico (all common sizes) plus a 256 px PNG.

It is the same mark as the Android app (res/drawable/app_icon.xml): a dark-green rounded square holding two woven
sound-wave lines, one mint and one teal, and a small gold dot. The paths below are copied from that file on a 48 x 48 grid.

Run with any Python that has Pillow:  python desktop/tools/make_icon.py
"""
import os

from PIL import Image, ImageDraw

SIZE = 1024                       # drawn large, then scaled down for smooth edges
SCALE = SIZE / 48.0
OUT = os.path.join(os.path.dirname(__file__), "..", "VoiceWeave.App", "Assets")
os.makedirs(OUT, exist_ok=True)

BACKGROUND = (0x10, 0x28, 0x21)
MINT = (0xA9, 0xF2, 0xCC)
TEAL = (0x63, 0xB9, 0x9C)
GOLD = (0xF2, 0xCB, 0x8C)


def cubic(p0, p1, p2, p3, steps=120):
    pts = []
    for i in range(steps + 1):
        t = i / steps
        u = 1 - t
        pts.append((
            u ** 3 * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t ** 3 * p3[0],
            u ** 3 * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t ** 3 * p3[1]))
    return pts


def smooth_path(start, first, rest):
    """SVG 'M start C c1 c2 end S c2 end S c2 end ...' -> list of points. `first` = (c1, c2, end); `rest` = [(c2, end), ...]."""
    pts = cubic(start, first[0], first[1], first[2])
    prev_c2, prev_end = first[1], first[2]
    for c2, end in rest:
        c1 = (2 * prev_end[0] - prev_c2[0], 2 * prev_end[1] - prev_c2[1])   # mirror of the previous control point
        pts += cubic(prev_end, c1, c2, end)[1:]
        prev_c2, prev_end = c2, end
    return pts


def stroke(draw, pts, color, width):
    """A round-capped stroke: stamp a filled circle along the path, densely enough that the edge is perfectly smooth."""
    r = width * SCALE / 2
    dense = []
    for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
        n = max(1, int(((x1 - x0) ** 2 + (y1 - y0) ** 2) ** 0.5 * SCALE / (r / 3)))
        dense += [(x0 + (x1 - x0) * i / n, y0 + (y1 - y0) * i / n) for i in range(n)]
    dense.append(pts[-1])
    for x, y in dense:
        draw.ellipse((x * SCALE - r, y * SCALE - r, x * SCALE + r, y * SCALE + r), fill=color)


def draw_icon():
    img = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.rounded_rectangle((0, 0, SIZE - 1, SIZE - 1), radius=int(12 * SCALE), fill=BACKGROUND + (255,))

    # M9,20 C14,20 14,12 19,12 S24,31 29,31 S34,20 39,20
    mint = smooth_path((9, 20), ((14, 20), (14, 12), (19, 12)), [((24, 31), (29, 31)), ((34, 20), (39, 20))])
    # M9,28 C14,28 14,36 19,36 S24,17 29,17 S34,28 36,28
    teal = smooth_path((9, 28), ((14, 28), (14, 36), (19, 36)), [((24, 17), (29, 17)), ((34, 28), (36, 28))])
    stroke(d, mint, MINT, 3.4)
    stroke(d, teal, TEAL, 3.4)
    cx, cy, r = 39.6, 28, 2.4
    d.ellipse(((cx - r) * SCALE, (cy - r) * SCALE, (cx + r) * SCALE, (cy + r) * SCALE), fill=GOLD)
    return img


icon = draw_icon()
sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
icon.resize((256, 256), Image.LANCZOS).save(os.path.join(OUT, "app.png"))
icon.save(os.path.join(OUT, "app.ico"), sizes=[(s, s) for s in sizes])
print("wrote", os.path.abspath(os.path.join(OUT, "app.ico")))
