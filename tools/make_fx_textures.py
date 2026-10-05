"""Small procedural textures (glows, particles, light brush, UI shapes) used by the runtime."""
from pathlib import Path

import numpy as np
from PIL import Image

OUT = Path(__file__).resolve().parent.parent / "Assets" / "Resources" / "Art" / "fx"
OUT.mkdir(parents=True, exist_ok=True)


def radial(name, size, stops):
    """White radial gradient whose alpha follows (offset, alpha) stops, like a canvas radial gradient."""
    y, x = np.mgrid[0:size, 0:size] + 0.5
    r = np.hypot(x - size / 2, y - size / 2) / (size / 2)
    offs, alphas = zip(*stops)
    a = np.interp(np.clip(r, 0, 1), offs, alphas)
    a[r > 1] = 0
    img = np.zeros((size, size, 4), np.uint8)
    img[..., :3] = 255
    img[..., 3] = np.round(a * 255).astype(np.uint8)
    Image.fromarray(img, "RGBA").save(OUT / f"{name}.png")


radial("glow", 256, [(0, 0.9), (0.25, 0.45), (0.6, 0.12), (1, 0)])
radial("spark", 32, [(0, 1), (0.4, 0.7), (1, 0)])
radial("smoke", 64, [(0, 0.9), (0.5, 0.4), (1, 0)])
radial("mote", 16, [(0, 1), (1, 0)])
# The light brush: a long smooth falloff reads as light rather than a hard spotlight.
radial("light_brush", 256, [(0, 1), (0.35, 0.95), (0.6, 0.6), (0.82, 0.18), (1, 0)])

# Vignette over the play view (stretched to the screen).
w, h = 320, 180
y, x = np.mgrid[0:h, 0:w] + 0.5
d = np.hypot((x - w / 2), (y - h / 2))
inner, outer = h * 0.35, w * 0.72
a = np.clip((d - inner) / (outer - inner), 0, 1) * 0.78
v = np.zeros((h, w, 4), np.uint8)
v[..., 0], v[..., 1], v[..., 2] = 2, 3, 8
v[..., 3] = np.round(a * 255).astype(np.uint8)
Image.fromarray(v, "RGBA").save(OUT / "vignette.png")

# Vertical gradients: canal water and the title shade.
def vgrad(name, h, top, bottom):
    t = np.linspace(0, 1, h)[:, None]
    g = np.zeros((h, 4, 4), np.uint8)
    for c in range(4):
        g[..., c] = np.round(np.repeat(top[c] + (bottom[c] - top[c]) * t, 4, axis=1))
    Image.fromarray(g, "RGBA").save(OUT / f"{name}.png")

vgrad("water", 64, (13, 20, 40, 242), (3, 5, 11, 242))
vgrad("shade", 64, (5, 6, 12, 38), (5, 6, 12, 217))

# Solid white square for rectangles, and a rounded rect + circle for UI (anti-aliased by supersampling).
Image.new("RGBA", (8, 8), (255, 255, 255, 255)).save(OUT / "white.png")

def shape(name, size, radius, ring=0):
    s = 4
    S = size * s
    y, x = np.mgrid[0:S, 0:S] + 0.5
    cx = np.clip(x, radius * s, S - radius * s)
    cy = np.clip(y, radius * s, S - radius * s)
    dist = np.hypot(x - cx, y - cy)
    inside = dist <= radius * s
    if ring:
        inside &= dist >= (radius - ring) * s
    m = inside.reshape(size, s, size, s).mean(axis=(1, 3))
    img = np.zeros((size, size, 4), np.uint8)
    img[..., :3] = 255
    img[..., 3] = np.round(m * 255).astype(np.uint8)
    Image.fromarray(img, "RGBA").save(OUT / f"{name}.png")

shape("round_rect", 32, 8)
shape("round_rect_ring", 32, 8, ring=1.2)
shape("circle", 128, 64)
shape("circle_ring", 128, 64, ring=2.5)
print("fx textures ->", OUT)
