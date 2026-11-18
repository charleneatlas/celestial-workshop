"""Standalone re-render of the in-game "Celestial Workshop" title treatment.

Source of truth (nothing here changes the game):
  * Lettering: Assets/CW_Art/Fonts/Meddon-Regular.ttf (SIL OFL 1.1), text
    "Celestial<space=0.85em>Workshop", no kerning, Title_Ink material
    (_FaceDilate 0.14, _ScaleRatioA 0.9, _GradientScale 10, atlas 90 pt).
  * Star band: a port of NoticeboardArt.TitleStars(1024, 200, seed 3) and the
    InkCanvas Stroke/Star/Dot routines, redrawn at high resolution.
  * Layouts: NoticeboardBuilder.BuildHeader and CreditsBoardBuilder (metres).

Usage: python -I render_wordmark.py <repo root> <out dir> [--verify]
"""
import math
import os
import sys

import numpy as np
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont
from PIL import Image, ImageDraw, ImageFont

# ---------------------------------------------------------------- colours
INK = (0.24, 0.13, 0.06)        # NoticeboardArt.Ink
GOLD = (0.80, 0.52, 0.16)       # NoticeboardArt.Gold
TITLE_INK = (0.22, 0.12, 0.05)  # NoticeboardBuilder.InkText

PALETTES = {
    # lettering + ink stars -> warm ivory, the gold feature star -> champagne gold
    "ivory": {"text": "#F4E9D2", "ink": "#F4E9D2", "gold": "#DDBB78"},
    # everything champagne, feature star a touch brighter
    "champagne": {"text": "#E6CB94", "ink": "#E6CB94", "gold": "#F2D9A2"},
}

# ---------------------------------------------------------------- layouts (metres)
LAYOUTS = {
    "noticeboard": dict(quad_c=(0.0, 0.203), quad=(0.44, 0.086), baseline=(0.0, 0.176), cap=0.034),
    "credits": dict(quad_c=(0.0, 0.100), quad=(0.30, 0.0586), baseline=(0.0, 0.083), cap=0.019),
}
CAPLINE, POINTSIZE = 98.0, 90.0          # Meddon SDF faceInfo
SPACE_EM = 0.85
DILATE_EM = (0.14 * 0.9 * 0.5) * 2 * 10 / POINTSIZE  # SDF threshold shift in atlas px -> em
STAR_TEX = (1024, 200)


def hex_rgb(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) / 255 for i in (0, 2, 4))


# ---------------------------------------------------------------- .NET System.Random (seeded)
class NetRandom:
    MBIG, MSEED = 2147483647, 161803398

    def __init__(self, seed):
        sa = [0] * 56
        mj = self.MSEED - abs(seed)
        sa[55] = mj
        mk = 1
        for i in range(1, 55):
            ii = (21 * i) % 55
            sa[ii] = mk
            mk = mj - mk
            if mk < 0:
                mk += self.MBIG
            mj = sa[ii]
        for _ in range(4):
            for i in range(1, 56):
                sa[i] -= sa[1 + (i + 30) % 55]
                if sa[i] < 0:
                    sa[i] += self.MBIG
        self.sa, self.inext, self.inextp = sa, 0, 21

    def next_double(self):
        self.inext = 1 if self.inext + 1 >= 56 else self.inext + 1
        self.inextp = 1 if self.inextp + 1 >= 56 else self.inextp + 1
        r = self.sa[self.inext] - self.sa[self.inextp]
        if r == self.MBIG:
            r -= 1
        if r < 0:
            r += self.MBIG
        self.sa[self.inext] = r
        return r * (1.0 / self.MBIG)


# ---------------------------------------------------------------- Perlin (Mathf.PerlinNoise stand-in)
_P = np.array([151,160,137,91,90,15,131,13,201,95,96,53,194,233,7,225,140,36,103,30,69,142,8,99,37,240,21,10,23,190,6,148,247,120,234,75,0,26,197,62,94,252,219,203,117,35,11,32,57,177,33,88,237,149,56,87,174,20,125,136,171,168,68,175,74,165,71,134,139,48,27,166,77,146,158,231,83,111,229,122,60,211,133,230,220,105,92,41,55,46,245,40,244,102,143,54,65,25,63,161,1,216,80,73,209,76,132,187,208,89,18,169,200,196,135,130,116,188,159,86,164,100,109,198,173,186,3,64,52,217,226,250,124,123,5,202,38,147,118,126,255,82,85,212,207,206,59,227,47,16,58,17,182,189,28,42,223,183,170,213,119,248,152,2,44,154,163,70,221,153,101,155,167,43,172,9,129,22,39,253,19,98,108,110,79,113,224,232,178,185,112,104,218,246,97,228,251,34,242,193,238,210,144,12,191,179,162,241,81,51,145,235,249,14,239,107,49,192,214,31,181,199,106,157,184,84,204,176,115,121,50,45,127,4,150,254,138,236,205,93,222,114,67,29,24,72,243,141,128,195,78,66,215,61,156,180]*2)


def perlin(x, y):
    x = np.asarray(x, float); y = np.asarray(y, float)
    xi = np.floor(x).astype(int) & 255; yi = np.floor(y).astype(int) & 255
    xf = x - np.floor(x); yf = y - np.floor(y)
    fade = lambda t: t * t * t * (t * (t * 6 - 15) + 10)
    u, v = fade(xf), fade(yf)

    def grad(h, gx, gy):
        h = h & 3
        return np.where(h & 1, -gx, gx) + np.where(h & 2, -gy, gy)

    aa = _P[_P[xi] + yi]; ab = _P[_P[xi] + yi + 1]
    ba = _P[_P[xi + 1] + yi]; bb = _P[_P[xi + 1] + yi + 1]
    x1 = grad(aa, xf, yf) * (1 - u) + grad(ba, xf - 1, yf) * u
    x2 = grad(ab, xf, yf - 1) * (1 - u) + grad(bb, xf - 1, yf - 1) * u
    return (x1 * (1 - v) + x2 * v + 1) * 0.5


# ---------------------------------------------------------------- TitleStars, as shapes in texture px (y up)
def title_stars_shapes(seed=3):
    w, h = STAR_TEX
    rnd = NetRandom(seed)
    shapes = []
    arc = []
    for i in range(81):
        t = i / 80
        arc.append((330 + (760 - 330) * t, h - 14 - ((t - 0.5) * 2) ** 2 * 16))
    shapes.append(("stroke", "ink", arc, 2.2, 0.45, seed))
    shapes.append(("star", "gold", (800, h - 22), 20, 4, 0.18, 0))
    shapes.append(("star", "gold", (800, h - 22), 12, 4, 0.22, 45))
    shapes.append(("star", "ink", (745, h - 14), 7, 4, 0.25, 0))
    spots = [(70, 150), (110, 60), (40, 95), (160, 178), (250, 186), (940, 160), (985, 80), (905, 40),
             (870, 186), (560, 192), (430, 188), (980, 130), (30, 30), (660, 190)]
    for p in spots:
        r = 5 + rnd.next_double() * 6
        eight = rnd.next_double() < 0.35
        shapes.append(("star", "ink", p, r, 4, 0.22, 0))
        if eight:
            shapes.append(("star", "ink", p, r * 0.6, 4, 0.25, 45))
    for _ in range(18):
        p = (rnd.next_double() * w, rnd.next_double() * h)
        if 150 < p[0] < 880 and p[1] < 160:
            continue
        shapes.append(("dot", "ink", p, 1.4 + rnd.next_double() * 1.4))
    return shapes


def star_poly(c, r, pts, inner, rot):
    out = []
    for i in range(pts * 2):
        a = i / (pts * 2) * math.pi * 2 + math.radians(rot + 90)
        rr = r if i % 2 == 0 else r * inner
        out.append((c[0] + math.cos(a) * rr, c[1] + math.sin(a) * rr))
    return out


def stroke_profile(pts, width, taper, seed):
    """Per-vertex half-width, same pressure model as InkCanvas.Stroke."""
    cum = [0.0]
    for a, b in zip(pts, pts[1:]):
        cum.append(cum[-1] + math.dist(a, b))
    total = cum[-1]
    taper_len = max(1e-3, taper * 0.5)
    s = np.array(cum) / total
    press = np.clip(np.minimum(s, 1 - s) / taper_len, 0, 1)
    press = 0.35 + 0.65 * np.sqrt(press)
    press *= 0.85 + 0.3 * perlin(s * 5, np.full_like(s, seed * 0.731))
    return s, width * 0.5 * press


# ---------------------------------------------------------------- raster helpers
def poly_mask(size, polys, ss=4):
    """Antialiased fill of polygons (list of [(x, y)] in image px, y down)."""
    W, H = size
    im = Image.new("L", (W * ss, H * ss), 0)
    d = ImageDraw.Draw(im)
    for poly in polys:
        d.polygon([(x * ss, y * ss) for x, y in poly], fill=255)
    return np.asarray(im.resize((W, H), Image.Resampling.BOX), float) / 255


def stroke_mask(size, pts_img, s, hw, seed, grain_scale):
    """Coverage of the tapered pen stroke (InkCanvas.Stroke), pts in image px."""
    W, H = size
    mask = np.zeros((H, W))
    pts = np.array(pts_img)
    pad = hw.max() + 2
    x0, y0 = int(max(0, pts[:, 0].min() - pad)), int(max(0, pts[:, 1].min() - pad))
    x1, y1 = int(min(W - 1, pts[:, 0].max() + pad)), int(min(H - 1, pts[:, 1].max() + pad))
    ys, xs = np.mgrid[y0:y1 + 1, x0:x1 + 1]
    px, py = xs + 0.5, ys + 0.5
    best = np.zeros(px.shape)
    for i in range(1, len(pts)):
        a, b = pts[i - 1], pts[i]
        ab = b - a
        l2 = max(1e-6, ab @ ab)
        t = np.clip(((px - a[0]) * ab[0] + (py - a[1]) * ab[1]) / l2, 0, 1)
        d = np.hypot(px - (a[0] + ab[0] * t), py - (a[1] + ab[1] * t))
        h = hw[i - 1] + (hw[i] - hw[i - 1]) * t
        best = np.maximum(best, np.clip(h - d + 0.5, 0, 1))
    best *= 0.84 + 0.16 * perlin(xs * 0.35 / grain_scale, ys * 0.35 / grain_scale)
    mask[y0:y1 + 1, x0:x1 + 1] = best
    return mask


def over(dst, rgb, cov):
    """Straight-alpha 'over' of a solid colour with coverage (InkCanvas.Over)."""
    a = np.clip(cov, 0, 1)
    da = dst[..., 3]
    na = a + da * (1 - a)
    safe = np.where(na > 1e-6, na, 1)
    for k in range(3):
        dst[..., k] = np.where(na > 1e-6, (rgb[k] * a + dst[..., k] * da * (1 - a)) / safe, dst[..., k])
    dst[..., 3] = na


# ---------------------------------------------------------------- the wordmark
class Wordmark:
    def __init__(self, repo, layout):
        self.ttf_path = os.path.join(repo, "Assets", "CW_Art", "Fonts", "Meddon-Regular.ttf")
        self.font = TTFont(self.ttf_path)
        self.upm = self.font["head"].unitsPerEm
        self.cmap = self.font.getBestCmap()
        self.L = LAYOUTS[layout]
        self.em = self.L["cap"] / (0.1 * CAPLINE / POINTSIZE) * 0.1  # metres per em (TMP fontSize * 0.1)

    def glyph_runs(self):
        """[(glyphName, x_em)] for 'Celestial' + 0.85em + 'Workshop', centred on advances."""
        hm = self.font["hmtx"]
        runs, x = [], 0.0
        for word_i, word in enumerate(("Celestial", "Workshop")):
            if word_i:
                x += SPACE_EM
            for ch in word:
                g = self.cmap[ord(ch)]
                runs.append((g, ch, x))
                x += hm[g][0] / self.upm
        return [(g, ch, gx - x / 2) for g, ch, gx in runs], x

    def frame(self, quad_px_width, margin_px):
        """Pixel mapping: the star band is quad_px_width wide; canvas cropped later."""
        L = self.L
        ppm = quad_px_width / L["quad"][0]
        return ppm

    # ---- raster
    def render(self, palette, quad_px_width=3600, margin_px=96, tex_res=None):
        L, P = self.L, {k: hex_rgb(v) for k, v in palette.items()}
        ppm = quad_px_width / L["quad"][0]
        qw, qh = L["quad"][0] * ppm, L["quad"][1] * ppm
        em_px = self.em * ppm
        # generous working canvas, metres origin at quad centre
        W = int(round(qw + 2 * margin_px)); H = int(round(qh + 2.2 * em_px + 2 * margin_px))
        cx, cy = W / 2, margin_px + qh / 2
        to_px = lambda mx, my: (cx + (mx - L["quad_c"][0]) * ppm, cy - (my - L["quad_c"][1]) * ppm)
        img = np.zeros((H, W, 4))

        # star band: texture px (y up) -> image px
        tw, th = STAR_TEX
        sx = qw / tw
        qx0, qy0 = cx - qw / 2, cy - qh / 2
        tex = lambda p: (qx0 + p[0] * sx, qy0 + (th - p[1]) * sx)
        for sh in title_stars_shapes():
            col = P[sh[1]]
            if sh[0] == "stroke":
                _, _, pts, width, taper, seed = sh
                s, hw = stroke_profile(pts, width, taper, seed)
                over(img, col, stroke_mask((W, H), [tex(p) for p in pts], s, hw * sx, seed, sx))
            elif sh[0] == "star":
                _, _, c, r, n, inner, rot = sh
                over(img, col, poly_mask((W, H), [[tex(p) for p in star_poly(c, r, n, inner, rot)]]))
            else:
                _, _, c, r = sh
                ys, xs = np.mgrid[0:H, 0:W]
                cc = tex(c)
                over(img, col, np.clip(r * sx - np.hypot(xs + 0.5 - cc[0], ys + 0.5 - cc[1]) + 0.5, 0, 1))

        # lettering: real outlines, dilated like the Title_Ink SDF material
        ss = 4
        size_px = em_px * ss
        pil_font = ImageFont.truetype(self.ttf_path, size=max(1, round(size_px)), layout_engine=ImageFont.Layout.BASIC)
        k = round(size_px) / size_px  # keep sub-pixel scale exact
        txt = Image.new("L", (W * ss, H * ss), 0)
        d = ImageDraw.Draw(txt)
        bx, by = to_px(*L["baseline"])
        runs, _ = self.glyph_runs()
        stroke = max(0, round(DILATE_EM * size_px))
        for g, ch, x_em in runs:
            d.text(((bx + x_em * em_px) * ss, by * ss), ch, font=pil_font, fill=255, anchor="ls",
                   stroke_width=stroke, stroke_fill=255)
        txt_mask = np.asarray(txt.resize((W, H), Image.Resampling.BOX), float) / 255
        over(img, P["text"], txt_mask)

        # crop vertically to content (+margin); keep horizontal centring on the band
        rows = np.where(img[..., 3].max(axis=1) > 1 / 255)[0]
        top, bot = max(0, rows[0] - margin_px), min(H, rows[-1] + 1 + margin_px)
        out = (np.clip(img[top:bot], 0, 1) * 255 + 0.5).astype(np.uint8)
        return Image.fromarray(out, "RGBA")

    # ---- vector
    def svg(self, palette, quad_w=1600):
        L = self.L
        ppm = quad_w / L["quad"][0]
        qw, qh = L["quad"][0] * ppm, L["quad"][1] * ppm
        em = self.em * ppm
        m = 40
        tw, th = STAR_TEX
        sx = qw / tw
        bx = 0.0
        by = (L["quad_c"][1] - L["baseline"][1]) * ppm  # baseline below band centre (y down)
        top = -qh / 2 - m
        bottom = max(qh / 2, by + 0.75 * em) + m
        W = qw + 2 * m
        tex = lambda p: (-qw / 2 + p[0] * sx, -qh / 2 + (th - p[1]) * sx)
        f = lambda v: f"{v:.2f}".rstrip("0").rstrip(".")
        parts = [f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{f(-W/2)} {f(top)} {f(W)} {f(bottom-top)}" '
                 f'width="{f(W)}" height="{f(bottom-top)}">',
                 '<title>Celestial Workshop</title>']
        for sh in title_stars_shapes():
            col = palette[sh[1]]
            if sh[0] == "stroke":
                _, _, pts, width, taper, seed = sh
                s, hw = stroke_profile(pts, width, taper, seed)
                P = np.array([tex(p) for p in pts]); hw = hw * sx
                tang = np.gradient(P, axis=0); tang /= np.linalg.norm(tang, axis=1)[:, None]
                nrm = np.stack([-tang[:, 1], tang[:, 0]], 1)
                left, right = P + nrm * hw[:, None], P - nrm * hw[:, None]
                ring = list(left) + list(right[::-1])
                parts.append(f'<path fill="{col}" d="M' + " L".join(f"{f(x)} {f(y)}" for x, y in ring) + 'Z"/>')
                for end, r in ((P[0], hw[0]), (P[-1], hw[-1])):
                    parts.append(f'<circle fill="{col}" cx="{f(end[0])}" cy="{f(end[1])}" r="{f(r)}"/>')
            elif sh[0] == "star":
                _, _, c, r, n, inner, rot = sh
                pts = [tex(p) for p in star_poly(c, r, n, inner, rot)]
                parts.append(f'<polygon fill="{col}" points="' + " ".join(f"{f(x)},{f(y)}" for x, y in pts) + '"/>')
            else:
                _, _, c, r = sh
                cc = tex(c)
                parts.append(f'<circle fill="{col}" cx="{f(cc[0])}" cy="{f(cc[1])}" r="{f(r*sx)}"/>')
        gs = self.font.getGlyphSet()
        runs, _ = self.glyph_runs()
        sc = em / self.upm
        dil = DILATE_EM * em * 2
        parts.append(f'<g fill="{palette["text"]}" stroke="{palette["text"]}" stroke-width="{f(dil)}" stroke-linejoin="round">')
        for g, ch, x_em in runs:
            pen = SVGPathPen(gs)
            gs[g].draw(TransformPen(pen, (sc, 0, 0, -sc, bx + x_em * em, by)))
            dpath = pen.getCommands()
            if dpath:
                parts.append(f'<path d="{dpath}"/>')
        parts.append("</g></svg>")
        return "\n".join(parts)


def verify(repo, out_dir):
    """Redraw the star band at 1x in the original ink and compare with the shipped texture."""
    ref = np.asarray(Image.open(os.path.join(repo, "Assets/CW_Onboarding/Art/Generated/TitleStars.png")).convert("RGBA"), float) / 255
    W, H = STAR_TEX
    img = np.zeros((H, W, 4))
    tex = lambda p: (p[0], H - p[1])
    P = {"ink": INK, "gold": GOLD}
    for sh in title_stars_shapes():
        if sh[0] == "stroke":
            _, k, pts, width, taper, seed = sh
            s, hw = stroke_profile(pts, width, taper, seed)
            over(img, P[k], stroke_mask((W, H), [tex(p) for p in pts], s, hw, seed, 1))
        elif sh[0] == "star":
            _, k, c, r, n, inner, rot = sh
            over(img, P[k], poly_mask((W, H), [[tex(p) for p in star_poly(c, r, n, inner, rot)]]))
        else:
            _, k, c, r = sh
            ys, xs = np.mgrid[0:H, 0:W]
            over(img, P[k], np.clip(r - np.hypot(xs + 0.5 - c[0], ys + 0.5 - H + c[1]) + 0.5, 0, 1))
    da = np.abs(img[..., 3] - ref[..., 3])
    print(f"star band alpha diff vs shipped texture: mean {da.mean():.4f}, max {da.max():.3f}, "
          f"pixels >0.25: {(da > 0.25).sum()}")
    cmp = np.concatenate([ref[..., 3], img[..., 3], da], 0)
    Image.fromarray((255 - cmp * 255).astype(np.uint8)).save(os.path.join(out_dir, "_verify_starband.png"))


def main():
    repo, out = sys.argv[1], sys.argv[2]
    os.makedirs(out, exist_ok=True)
    if "--verify" in sys.argv:
        verify(repo, out)
        return
    for layout in ("noticeboard", "credits"):
        wm = Wordmark(repo, layout)
        for pname, pal in PALETTES.items():
            base = f"CelestialWorkshop_Wordmark_{layout}_{pname}"
            png = wm.render(pal)
            png.save(os.path.join(out, base + ".png"), optimize=True)
            with open(os.path.join(out, base + ".svg"), "w", encoding="utf-8") as fh:
                fh.write(wm.svg(pal))
            # 1920x1080 preview: wordmark ~60% of frame width, centred on pure black
            target_w = 1150
            small = png.resize((target_w, round(png.height * target_w / png.width)), Image.Resampling.LANCZOS)
            bg = Image.new("RGBA", (1920, 1080), (0, 0, 0, 255))
            bg.alpha_composite(small, ((1920 - small.width) // 2, (1080 - small.height) // 2))
            bg.convert("RGB").save(os.path.join(out, base + "_preview_1920x1080.png"))
            print(base, png.size)


if __name__ == "__main__":
    main()
