"""YouTube thumbnail from the Cassiopeia video frame + the r-fixed wordmark.

Usage: python make_thumbnail.py <frame.png> <wordmark.png> <out dir>
Writes thumbnail_3840x2160.png (master) and thumbnail_1280x720.jpg (upload, < 2 MB).
Coordinates below are in the 1920x1080 source frame.
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFilter

S = 2  # work at 4K
frame_p, logo_p, out = sys.argv[1:4]
src = Image.open(frame_p).convert("RGB")
assert src.size == (1920, 1080), src.size
W, H = 1920 * S, 1080 * S

# ---- upscale + detail
img = src.resize((W, H), Image.Resampling.LANCZOS)
img = img.filter(ImageFilter.UnsharpMask(radius=2.4 * S, percent=70, threshold=2))


def mask(draw_fn, feather):
    m = Image.new("L", (W, H), 0)
    draw_fn(ImageDraw.Draw(m))
    return m.filter(ImageFilter.GaussianBlur(feather * S))


sc = lambda *v: [x * S for x in v]
GLOBE = (1092, 392, 312)          # cx, cy, r
CHART = (748, 878, 172)
subject = mask(lambda d: (
    d.ellipse(sc(GLOBE[0] - GLOBE[2], GLOBE[1] - GLOBE[2], GLOBE[0] + GLOBE[2], GLOBE[1] + GLOBE[2]), fill=255),
    d.ellipse(sc(CHART[0] - CHART[2], CHART[1] - CHART[2], CHART[0] + CHART[2], CHART[1] + CHART[2]), fill=255),
    d.ellipse(sc(915, 880, 1375, 1000), fill=255),                     # ring + sculpture
    d.polygon(sc(1330, 900, 1440, 860, 1560, 880, 1640, 1080, 1360, 1080, 1330, 960), fill=255),  # hand
    d.rounded_rectangle(sc(1462, 722, 1702, 872), radius=12 * S, fill=255),  # back-to-title plaque
), 10)

# ---- background: blur, slightly darker/cooler so the glowing globe pops
bg = img.filter(ImageFilter.GaussianBlur(7 * S))
a = np.asarray(img, float) / 255
b = np.asarray(bg, float) / 255
m = np.asarray(subject, float)[..., None] / 255
b = b * 0.88
x = b * (1 - m) + a * m

# ---- grade: contrast S-curve + saturation
lum = (x * [0.2126, 0.7152, 0.0722]).sum(-1, keepdims=True)
x = lum + (x - lum) * 1.25
x = np.clip(x, 0, 1)
x = x * x * (3 - 2 * x) * 0.45 + x * 0.55              # S-curve blend
x = np.clip((x - 0.02) / 0.98, 0, 1)

# ---- glow on the action: constellation lines/stars, globe rim, ring
glow_src = np.asarray(img, float) / 255
gl = glow_src.max(-1)
blueish = (glow_src[..., 2] - glow_src[..., 0])
inside_globe = np.asarray(mask(lambda d: d.ellipse(sc(GLOBE[0] - GLOBE[2] - 30, GLOBE[1] - GLOBE[2] - 30,
                                                     GLOBE[0] + GLOBE[2] + 30, GLOBE[1] + GLOBE[2] + 30)), 4), float) / 255
ring_zone = np.asarray(mask(lambda d: d.ellipse(sc(910, 895, 1365, 985), fill=255), 6), float) / 255
inside_globe = np.asarray(mask(lambda d: d.ellipse(sc(GLOBE[0] - GLOBE[2] - 30, GLOBE[1] - GLOBE[2] - 30,
                                                     GLOBE[0] + GLOBE[2] + 30, GLOBE[1] + GLOBE[2] - 120), fill=255), 6), float) / 255
emit = np.clip((gl - 0.45) / 0.4, 0, 1) * np.clip(blueish * 4 + 0.3, 0, 1) * np.maximum(inside_globe, ring_zone)
emit_rgb = (np.stack([emit] * 3, -1) * [0.55, 0.78, 1.0] * 255).astype(np.uint8)
e = Image.fromarray(emit_rgb)
glow = (np.asarray(e.filter(ImageFilter.GaussianBlur(6 * S)), float) * 2.0
        + np.asarray(e.filter(ImageFilter.GaussianBlur(22 * S)), float) * 1.2) / 255
x = 1 - (1 - x) * (1 - np.clip(glow, 0, 1))             # screen

# globe atmosphere: soft blue halo around the sphere
yy, xx = np.mgrid[0:H, 0:W]
dist = np.hypot(xx - GLOBE[0] * S, yy - GLOBE[1] * S) / S
halo = np.exp(-((dist - GLOBE[2]) / 38) ** 2) * (dist > GLOBE[2] - 12) * 0.22
x = 1 - (1 - x) * (1 - halo[..., None] * [0.10, 0.40, 1.0])

# ---- vignette + a darker top-left corner as a clean field for the wordmark
v = np.hypot((xx / W - 0.55) * 1.2, (yy / H - 0.48)) 
x *= np.clip(1.08 - v * 0.75, 0.55, 1)[..., None]
corner = np.clip(1 - np.hypot(xx / W / 0.52, yy / H / 0.42), 0, 1) ** 1.2
x *= (1 - corner * 0.55)[..., None]
x = np.clip(x, 0, 1)
out_img = Image.fromarray((x * 255 + 0.5).astype(np.uint8))

# ---- wordmark, top-left
logo = Image.open(logo_p).convert("RGBA")
alpha = np.asarray(logo)[..., 3]
ys, xs = np.where(alpha > 8)
logo = logo.crop((xs.min(), ys.min(), xs.max() + 1, ys.max() + 1))
lw = 760 * S
logo = logo.resize((lw, round(logo.height * lw / logo.width)), Image.Resampling.LANCZOS)
pos = (28 * S, 40 * S)
shadow = Image.new("RGBA", logo.size, (0, 0, 0, 0))
shadow.putalpha(logo.getchannel("A").point(lambda v: int(v * 0.85)))
pad = 40 * S
sh = Image.new("RGBA", (logo.width + 2 * pad, logo.height + 2 * pad), (0, 0, 0, 0))
sh.paste(shadow, (pad, pad))
sh = sh.filter(ImageFilter.GaussianBlur(9 * S))
glowl = Image.new("RGBA", sh.size, (0, 0, 0, 0))
gold = Image.new("RGBA", logo.size, (242, 217, 162, 0)); gold.putalpha(logo.getchannel("A").point(lambda v: int(v * 0.45)))
glowl.paste(gold, (pad, pad)); glowl = glowl.filter(ImageFilter.GaussianBlur(14 * S))
base = out_img.convert("RGBA")
base.alpha_composite(sh, (pos[0] - pad + 3 * S, pos[1] - pad + 5 * S))
base.alpha_composite(glowl, (pos[0] - pad, pos[1] - pad))
base.alpha_composite(logo, pos)
final = base.convert("RGB")

os.makedirs(out, exist_ok=True)
final.save(os.path.join(out, "thumbnail_3840x2160.png"), optimize=True)
small = final.resize((1280, 720), Image.Resampling.LANCZOS).filter(ImageFilter.UnsharpMask(1.0, 40, 1))
small.save(os.path.join(out, "thumbnail_1280x720.jpg"), quality=92, subsampling=0)
print(os.path.getsize(os.path.join(out, "thumbnail_1280x720.jpg")))
