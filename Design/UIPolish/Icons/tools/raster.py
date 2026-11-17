"""Rasterise the icons with headless Chrome (black on white at 1024 px), then
build Unity PNGs with Pillow: alpha = coverage, RGB = white everywhere (no dark
fringe under bilinear/mip filtering), plus brown preview PNGs.
"""
import os, subprocess, sys
from PIL import Image

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import icons

HERE = os.path.dirname(os.path.abspath(__file__))
BASE = os.path.abspath(os.path.join(HERE, ".."))
TMP = os.path.join(BASE, "tmp_render")
CHROME = r"C:\Program Files\Google\Chrome\Application\chrome.exe"
CELL = 1024
SIZES = (256, 128)

os.makedirs(TMP, exist_ok=True)
for d in ("png_white", "png_brown_preview"):
    os.makedirs(os.path.join(BASE, d), exist_ok=True)

for name, label, concept, fn in icons.ICONS:
    html = ('<!doctype html><html><head><style>html,body{margin:0;background:#fff;overflow:hidden}'
            'svg{display:block}</style></head><body>%s</body></html>') % icons.svg(fn(), "#000000", CELL)
    hp = os.path.join(TMP, name + ".html")
    open(hp, "w", encoding="utf-8").write(html)
    shot = os.path.join(TMP, name + ".png")
    subprocess.run([CHROME, "--headless=new", "--disable-gpu", "--hide-scrollbars",
                    "--force-device-scale-factor=1", "--window-size=%d,%d" % (CELL, CELL),
                    "--user-data-dir=" + os.path.join(TMP, "profile"),
                    "--screenshot=" + shot, "file:///" + hp.replace("\\", "/")],
                   check=True, capture_output=True, timeout=60)
    g = Image.open(shot).convert("L").crop((0, 0, CELL, CELL))
    cov = g.point(lambda v: 255 - v)
    for s in SIZES:
        a = cov.resize((s, s), Image.LANCZOS)
        white = Image.merge("RGBA", (Image.new("L", (s, s), 255),) * 3 + (a,))
        suffix = "" if s == 256 else "_%d" % s
        white.save(os.path.join(BASE, "png_white", name + suffix + ".png"))
        if s == 256:
            r, gg, b = 0x38, 0x1F, 0x0D
            brown = Image.merge("RGBA", (Image.new("L", (s, s), r), Image.new("L", (s, s), gg), Image.new("L", (s, s), b), a))
            brown.save(os.path.join(BASE, "png_brown_preview", name + "_brown.png"))
    print("ok", name)
