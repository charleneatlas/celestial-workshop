"""Build ../preview.html: self-contained sheet with inline SVG icons."""
import base64, os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import icons

BASE = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
FONTS = r"C:\Users\drumm\Github\celestial-workshop\Assets\CW_Art\Fonts"


def font_face(family, fname):
    data = base64.b64encode(open(os.path.join(FONTS, fname), "rb").read()).decode()
    return "@font-face{font-family:'%s';src:url(data:font/ttf;base64,%s) format('truetype');}" % (family, data)


def sym(name, fn):
    body = "\n".join(fn()).replace("INK", "currentColor")
    return ('<symbol id="%s" viewBox="0 0 48 48"><g fill="none" stroke="currentColor" stroke-width="%s" '
            'stroke-linecap="round" stroke-linejoin="round">%s</g></symbol>') % (name, icons.MAIN, body)


def use(name, px, cls=""):
    return '<svg class="%s" width="%d" height="%d" aria-hidden="true"><use href="#%s"/></svg>' % (cls, px, px, name)


cards = []
for name, label, concept, fn in icons.ICONS:
    smalls = "".join('<span class="sz">%s<i>%dpx</i></span>' % (use(name, px), px) for px in (48, 32, 24, 16))
    surfaces = "".join(
        '<div class="surf %s">%s%s</div>' % (s, use(name, 40), use(name, 22)) for s in ("parch", "wood", "brass"))
    btn = ('<div class="btnrow"><div class="btn">%s<span>%s</span></div>'
           '<div class="btn pressed">%s<span>%s</span></div></div>') % (
        use(name, 24), label.split(" (")[0].split(" / ")[0], use(name, 24), "pressed")
    vr = '<canvas class="vr" data-icon="%s" width="28" height="28"></canvas>' % name
    cards.append('<article class="card"><div class="big">%s</div><h2>%s</h2><p class="file">%s.svg</p>'
                 '<p class="concept">%s</p><div class="row">%s</div><div class="row">%s</div>'
                 '<div class="row vrrow">%s<span class="note">~28 headset px (9.6 mm at 0.5 m on Quest 3), shown pixel-enlarged</span></div>%s</article>'
                 % (use(name, 160), label, name, concept, smalls, surfaces, vr, btn))

symbols = "".join(sym(n, fn) for n, _, _, fn in icons.ICONS)
raw = {n: icons.svg(fn(), "#381F0D") for n, _, _, fn in icons.ICONS}
import json

html = """<!doctype html>
<html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Workshop Icon Set</title>
<style>
%s%s
:root{--ink:#381F0D;--parch:#EDDBB3;--parch2:#F4E7C8;--wood:#3C2415;--woodp:#80522B;--cream:#FFF0CC;--brass:#BD8033;--gold:#CC8529}
*{box-sizing:border-box}
body{margin:0;background:#E9DCC0;color:var(--ink);font:15px/1.45 'Architects Daughter',Georgia,serif;padding:24px 16px 48px}
header{max-width:1180px;margin:0 auto 20px}
h1{font-family:'Meddon',cursive;font-weight:400;font-size:40px;margin:0 0 4px}
header p{margin:4px 0;max-width:820px}
.grid{max-width:1180px;margin:0 auto;display:grid;grid-template-columns:repeat(auto-fill,minmax(340px,1fr));gap:16px}
.card{background:var(--parch2);border:2px solid #8a6a45;border-radius:10px;padding:14px;box-shadow:0 2px 0 #b89b72}
.big{display:flex;justify-content:center;background:radial-gradient(circle at 50%% 40%%,#FBF3DF,var(--parch));border-radius:8px;padding:10px;color:var(--ink)}
h2{font-size:18px;margin:10px 0 0}
.file{margin:0;font-family:Consolas,monospace;font-size:12px;opacity:.75}
.concept{margin:2px 0 8px}
.row{display:flex;gap:10px;align-items:flex-end;flex-wrap:wrap;margin:8px 0}
.sz{display:flex;flex-direction:column;align-items:center;color:var(--ink)}
.sz i{font-style:normal;font-size:11px;font-family:Consolas,monospace}
.surf{display:flex;gap:10px;align-items:center;padding:8px 12px;border-radius:6px;flex:1;justify-content:center;min-width:90px}
.parch{background:repeating-linear-gradient(8deg,#EBD7AC 0 3px,#EFDDB6 3px 7px);color:var(--ink)}
.wood{background:repeating-linear-gradient(92deg,#3C2415 0 4px,#4A2D1A 4px 7px,#35200F 7px 12px);color:var(--cream)}
.brass{background:linear-gradient(160deg,#E2B566,#B47A2E 45%%,#8E5A1E 70%%,#C99447);color:#2E1806;box-shadow:inset 0 0 0 2px #6E4314}
.vrrow{align-items:center}
canvas.vr{width:112px;height:112px;image-rendering:pixelated;background:var(--parch);border-radius:6px}
.note{font-size:12px;max-width:180px}
.btnrow{display:flex;gap:10px;padding:10px;border-radius:8px;background:repeating-linear-gradient(92deg,#3C2415 0 4px,#4A2D1A 4px 7px,#35200F 7px 12px)}
.btn{width:104px;height:84px;border-radius:12px;background:var(--parch);display:flex;flex-direction:column;align-items:center;justify-content:center;gap:4px;color:var(--ink);font-weight:700;font-size:13px;text-align:center;line-height:1.1;padding:4px}
.btn.pressed{background:var(--woodp);color:var(--cream)}
@media (max-width:420px){.grid{grid-template-columns:1fr}}
</style></head><body>
<svg width="0" height="0" style="position:absolute">%s</svg>
<header>
<h1>Celestial Workshop &middot; navigation icons</h1>
<p>Twelve original single-colour engravings for the ISDK button Icon slot (24 &times; 24 units = 12 mm at canvas scale 0.0005; about 9.6 mm on the title row at scale 0.8). Line weight 2.0 units in the 24 slot, details 1.5 to 1.6, round caps. White PNGs tint to ink <b>(0.22, 0.12, 0.05)</b> through the WorkshopButton animator clips, and to cream on press.</p>
<p>Each card: large view, small sizes, the glyph on parchment / wood / brass, a pixel-enlarged headset-resolution simulation, and a mock workshop button (normal and pressed).</p>
</header>
<main class="grid">%s</main>
<script>
const RAW=%s;
document.querySelectorAll('canvas.vr').forEach(c=>{const img=new Image();img.onload=()=>{const x=c.getContext('2d');x.imageSmoothingEnabled=true;x.drawImage(img,0,0,28,28)};img.src='data:image/svg+xml;charset=utf-8,'+encodeURIComponent(RAW[c.dataset.icon].replace('<svg ','<svg width="28" height="28" '));});
</script>
</body></html>""" % (font_face("Architects Daughter", "ArchitectsDaughter-Regular.ttf"), font_face("Meddon", "Meddon-Regular.ttf"),
                     symbols, "\n".join(cards), json.dumps(raw))

open(os.path.join(BASE, "preview.html"), "w", encoding="utf-8").write(html)
print("preview.html", len(html))
