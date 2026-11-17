"""Rough TMP autosize/wrap simulation for the Credits body (ArchitectsDaughter).
usage: python -I fit.py <font.ttf> <credits.txt> [bodyTop bodyBottom]"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from ttfadv import advances

chars = advances(sys.argv[1])
src = open(sys.argv[2], encoding='utf-8-sig').read()
top, bottom = (float(sys.argv[3]), float(sys.argv[4])) if len(sys.argv) > 4 else (0.027, -0.112)
pt, lh, cap = 90.0, 125.06836, 65.0
W, H = 0.36, top - bottom
missing = sorted({c for c in src if ord(c) not in chars and c not in '\r\n'})


def width(s, fs):
    return sum(chars.get(ord(c), 45.0) for c in s) / pt * fs * 0.1


def layout(fs):
    em = fs * 0.1
    lines, any_, gap, n, wrapped = 0.0, False, False, 0, []
    for raw in src.replace('\r\n', '\n').split('\n'):
        line = raw.rstrip()
        if not line:
            gap = any_
            continue
        if gap:
            lines += 0.55
            gap = False
        scale = 1.3 if line.startswith('## ') else 1.0
        text = line[3:] if scale > 1 else line
        cur, k = '', 1
        for w in text.split(' '):
            t = (cur + ' ' + w).strip()
            if width(t, fs * scale) > W and cur:
                k += 1
                cur = w
            else:
                cur = t
        if k > 1:
            wrapped.append((k, text[:40]))
        lines += k * scale
        any_ = True
        n += k
    return lines * (em * (lh / pt) - em * 0.08), n, wrapped


fsmax = 0.0074 / (0.1 * cap / pt)
fsmin = 0.0052 / (0.1 * cap / pt)
fs = fsmax
while fs > fsmin:
    if layout(fs)[0] <= H:
        break
    fs -= 0.0005
fs = max(fs, fsmin)
h, n, wrapped = layout(fs)
c = fs * 0.1 * cap / pt
print(f"missing glyphs: {missing}")
print(f"box {H*1000:.0f} mm; cap {c*1000:.2f} mm (range 5.2-7.4); text height {h*1000:.0f} mm; lines {n}; fits={h <= H + 1e-9}")
print(f"angular cap at 0.51 m: {c/0.51*57.2958:.2f} deg (Meta legible >= 0.64, comfortable ~0.82)")
for k, t in wrapped:
    print(f"  wraps to {k}: {t}")
