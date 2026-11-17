"""Celestial Workshop navigation icons: original, hand-authored SVG geometry.

All icons live on a 48 x 48 grid (2x the ISDK 24-unit icon slot), single colour,
round caps/joins. Main stroke 4.0 (= 2.0 in the 24 slot), detail stroke >= 3.0.
Run: python icons.py  -> writes ../svg/*.svg
"""
import math, os

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "svg")
INK = "#381F0D"  # (0.22, 0.12, 0.05)
MAIN = 4.0
DETAIL = 3.2


def f(v):
    return ("%.2f" % v).rstrip("0").rstrip(".")


def sparkle(cx, cy, r, waist=None):
    """Four-point star, as on the noticeboard SparkleStar (filled)."""
    w = r * 0.18 if waist is None else waist
    pts = [(cx, cy - r), (cx + r, cy), (cx, cy + r), (cx - r, cy)]
    ctrl = [(cx + w, cy - w), (cx + w, cy + w), (cx - w, cy + w), (cx - w, cy - w)]
    d = "M%s %s" % (f(pts[0][0]), f(pts[0][1]))
    for i in range(4):
        c = ctrl[i]
        p = pts[(i + 1) % 4]
        d += " Q%s %s %s %s" % (f(c[0]), f(c[1]), f(p[0]), f(p[1]))
    return '<path d="%s Z" fill="INK" stroke="INK" stroke-width="1.6"/>' % d


def head(tip, direction, length=7.5, spread=0.62, filled=True):
    """Arrowhead at tip pointing along direction (dx, dy)."""
    dx, dy = direction
    n = math.hypot(dx, dy)
    dx, dy = dx / n, dy / n
    bx, by = tip[0] - dx * length, tip[1] - dy * length
    px, py = -dy * length * spread, dx * length * spread
    a = (bx + px, by + py)
    b = (bx - px, by - py)
    if filled:
        return '<path d="M%s %s L%s %s L%s %s Z" fill="INK" stroke="INK" stroke-width="2.4"/>' % (
            f(tip[0]), f(tip[1]), f(a[0]), f(a[1]), f(b[0]), f(b[1]))
    return '<path d="M%s %s L%s %s L%s %s" fill="none"/>' % (
        f(a[0]), f(a[1]), f(tip[0]), f(tip[1]), f(b[0]), f(b[1]))


def polar(cx, cy, r, deg):
    a = math.radians(deg)
    return (cx + r * math.cos(a), cy + r * math.sin(a))


# ---------------------------------------------------------------- icons

def start():
    # Astrolabe: the mater hangs from its suspension ring; the rule-pointer says "go".
    return [
        '<circle cx="24" cy="6.5" r="3.3" stroke-width="%s"/>' % DETAIL,
        '<path d="M24 9.8 L24 11.5" stroke-width="%s"/>' % DETAIL,
        '<circle cx="24" cy="27" r="15.5"/>',
        '<path d="M20.5 19 L31.5 27 L20.5 35 Z" fill="INK" stroke-width="3"/>',
    ]


def recenter():
    # Sighting reticle: a small ring, four engraved pointers closing on the centre.
    s = ['<circle cx="24" cy="24" r="8.5"/>',
         '<circle cx="24" cy="24" r="2.6" fill="INK" stroke="none"/>']
    for deg in (0, 90, 180, 270):
        o = polar(24, 24, 20.5, deg)
        i = polar(24, 24, 12.5, deg)
        s.append('<path d="M%s %s L%s %s" />' % (f(o[0]), f(o[1]), f(i[0]), f(i[1])))
        s.append(head(i, (i[0] - o[0], i[1] - o[1]), length=5.2, spread=0.75, filled=False))
    return s


def _raise_body():
    # Fletched engraved arrow standing on a spirit level.
    return [
        '<rect x="8" y="37.5" width="32" height="6.5" rx="3.25"/>',
        '<circle cx="24" cy="40.75" r="1.9" fill="INK" stroke="none"/>',
        '<path d="M24 31 L24 13"/>',
        '<path d="M24 5 L32.5 15.5 L15.5 15.5 Z" fill="INK" stroke-width="2.6"/>',
        '<path d="M19.5 33 L24 29 L28.5 33" stroke-width="%s"/>' % DETAIL,
    ]


def raise_():
    return _raise_body()


def lower():
    return ['<g transform="matrix(1 0 0 -1 0 48)">'] + _raise_body() + ['</g>']


def credits():
    # Quill with an ink flourish.
    return [
        '<path d="M14 33.5 C15 22 27 10 41 6.5 C39.5 19.5 27 32 14 33.5 Z" stroke-width="3.4"/>',
        '<path d="M6.5 41.5 L33 15"/>',
        '<path d="M21.5 26.5 L18.6 21.6" stroke-width="2.8"/>',
        '<path d="M27.5 20.5 L25.8 14.4" stroke-width="2.8"/>',
        '<path d="M24 41.5 C27.5 38 30.5 44.5 34.5 41 C36.5 39.3 38.5 41.5 41.5 40" stroke-width="%s"/>' % DETAIL,
    ]


def back():
    # Engraved return arrow (a U-turn), filled head.
    return [
        '<path d="M38 41 L38 30 C38 21 32 16 23 16 L15 16"/>',
        '<path d="M6.5 16 L17 7.5 L17 24.5 Z" fill="INK" stroke-width="2.6"/>',
        '<path d="M34 43 L38 39 L42 43" stroke-width="%s"/>' % DETAIL,
    ]


def back_to_title():
    # Return arrow bringing you back to the star (the title sky).
    return [
        sparkle(11, 14, 8.5, 1.9),
        '<path d="M40 41 L40 34 C40 24 34 20.5 27 20.5 L26 20.5"/>',
        '<path d="M36 44 L40 40 L44 44" stroke-width="%s"/>' % DETAIL,
        '<path d="M18.5 20.5 L28 12.5 L28 28.5 Z" fill="INK" stroke-width="2.6"/>',
    ]


def field_note():
    # Folded note with the Cassiopeia-like star line inside.
    return [
        '<path d="M11 5.5 L29.5 5.5 L37.5 13.5 L37.5 42.5 L11 42.5 Z"/>',
        '<path d="M29.5 5.5 L29.5 13.5 L37.5 13.5" stroke-width="%s"/>' % DETAIL,
        '<path d="M17 35 L21.5 25 L26 30 L31.5 21" stroke-width="2.8"/>',
        '<circle cx="17" cy="35" r="2.7" fill="INK" stroke="none"/>',
        '<circle cx="21.5" cy="25" r="2.7" fill="INK" stroke="none"/>',
        '<circle cx="26" cy="30" r="2.7" fill="INK" stroke="none"/>',
        '<circle cx="31.5" cy="21" r="2.7" fill="INK" stroke="none"/>',
    ]


def _crate(x, y, w, h):
    return [
        '<rect x="%s" y="%s" width="%s" height="%s" rx="1.5"/>' % (f(x), f(y), f(w), f(h)),
        '<path d="M%s %s L%s %s" stroke-width="%s"/>' % (f(x), f(y + 5), f(x + w), f(y + 5), DETAIL),
        '<rect x="%s" y="%s" width="3" height="4" fill="INK" stroke-width="1.6"/>' % (f(x + w / 2 - 1.5), f(y + 4.2)),
    ]


def all_boxes():
    # Three constellation boxes stacked, lids and clasps.
    return _crate(4.5, 27.5, 18, 15) + _crate(25.5, 27.5, 18, 15) + _crate(15, 8.5, 18, 15)


def another_box():
    # An open box with a star rising from it (Choose Another Box / Enter the Workshop).
    return [
        '<rect x="8" y="27" width="32" height="15.5" rx="1.5"/>',
        '<path d="M8 27 L12.5 18.5 L35.5 18.5 L40 27" stroke-width="3.6"/>',
        '<rect x="22.3" y="26.5" width="3.4" height="5.5" fill="INK" stroke-width="1.6"/>',
        sparkle(24, 9.5, 7, 1.6),
    ]


def try_another():
    # Armillary turn: two orbit arcs chasing each other round a star.
    s = []
    cx, cy, r = 24, 24, 16
    for a0, a1 in ((205, 330), (25, 150)):
        p0, p1 = polar(cx, cy, r, a0), polar(cx, cy, r, a1)
        s.append('<path d="M%s %s A%s %s 0 0 1 %s %s"/>' % (f(p0[0]), f(p0[1]), r, r, f(p1[0]), f(p1[1])))
        t = math.radians(a1)
        s.append(head(polar(cx, cy, r, a1 + 9), (-math.sin(t), math.cos(t)), length=8, spread=0.62))
    s.append(sparkle(24, 24, 7.5, 1.7))
    return s


def view_constellations():
    # Spyglass on a small tripod, sighting a star.
    return [
        '<g transform="rotate(-28 22 24)">'
        '<rect x="4.5" y="20.5" width="8" height="7" rx="1.2"/>'
        '<rect x="12.5" y="19" width="11" height="10" rx="1.2"/>'
        '<rect x="23.5" y="17" width="12.5" height="14" rx="1.5"/>'
        '</g>',
        '<path d="M21 30 L14 43.5 M21 30 L28 43.5" stroke-width="3.4"/>',
        sparkle(39.5, 8.5, 6.5, 1.5),
    ]


ICONS = [
    # file, action label(s), concept, builder
    ("cw_icon_start", "Start", "Hanging astrolabe whose rule points 'go'", start),
    ("cw_icon_recenter", "Recenter", "Sighting reticle: four engraved pointers closing on centre", recenter),
    ("cw_icon_raise", "Raise", "Fletched arrow rising from a spirit level", raise_),
    ("cw_icon_lower", "Lower", "Fletched arrow descending onto a spirit level", lower),
    ("cw_icon_credits", "Credits", "Quill with an ink flourish", credits),
    ("cw_icon_back", "Back (Credits board, Field Note reader)", "Fletched U-turn return arrow", back),
    ("cw_icon_back_to_title", "Back to title", "Fletched return arrow homing on the title star", back_to_title),
    ("cw_icon_field_note", "Field Note", "Dog-eared note with a star-chart line", field_note),
    ("cw_icon_all_boxes", "All boxes", "Three stacked constellation boxes", all_boxes),
    ("cw_icon_try_another", "Choose Another Constellation / Try Another Letter", "Armillary turn: two orbit arrows round a star", try_another),
    ("cw_icon_another_box", "Choose Another Box / Enter the Workshop", "Open box with a star rising", another_box),
    ("cw_icon_view_constellations", "View Constellations (button currently hidden)", "Spyglass on a tripod sighting a star", view_constellations),
]


def svg(parts, colour=INK, size=None):
    body = "\n  ".join(parts).replace("INK", colour)
    dims = ' width="%d" height="%d"' % (size, size) if size else ""
    return ('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 48 48"%s fill="none" stroke="%s" '
            'stroke-width="%s" stroke-linecap="round" stroke-linejoin="round">\n  %s\n</svg>\n') % (dims, colour, MAIN, body)


if __name__ == "__main__":
    os.makedirs(OUT, exist_ok=True)
    for name, label, concept, fn in ICONS:
        with open(os.path.join(OUT, name + ".svg"), "w", encoding="utf-8") as fh:
            fh.write("<!-- Celestial Workshop icon: %s. Original work, no third-party artwork. -->\n" % label)
            fh.write(svg(fn()))
    print("wrote", len(ICONS))
