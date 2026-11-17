import re
R=r"C:\Users\drumm\Github\celestial-workshop\Assets\CW_Art\Fonts\\"
def load(n):
    t=open(R+n+" SDF.asset",encoding="utf-8").read()
    adv={}
    for m in re.finditer(r"m_Index: (\d+)\s+m_Metrics:\s+m_Width: [\d.]+\s+m_Height: [\d.]+\s+m_HorizontalBearingX: [-\d.e]+\s+m_HorizontalBearingY: [-\d.e]+\s+m_HorizontalAdvance: ([\d.e-]+)",t):
        adv[int(m.group(1))]=float(m.group(2))
    cm={}
    for m in re.finditer(r"m_Unicode: (\d+)\s+m_GlyphIndex: (\d+)",t):
        cm[int(m.group(1))]=int(m.group(2))
    return adv,cm
F={n:load(n) for n in ["Caveat","ArchitectsDaughter","Meddon"]}
def w(font,s):
    adv,cm=F[font]; miss=0; tot=0
    for ch in s:
        g=cm.get(ord(ch))
        if g is None or g not in adv: miss+=1; tot+=45; continue
        tot+=adv[g]
    return tot/90.0, miss   # in em
for f in F: print(f, len(F[f][0]), "glyphs", "avg lowercase em", round(w(f,"the quick brown fox jumps over a lazy dog")[0]/41,3))
tests=[("Caveat","Touch a star on your sketch to find it in the model and sky"),("Caveat","Turn the model until the sky matches your sketch"),
("ArchitectsDaughter","Back to title"),("ArchitectsDaughter","Field Note"),("ArchitectsDaughter","Choose Another Constellation"),("ArchitectsDaughter","Try Another Letter"),("ArchitectsDaughter","Enter the Workshop"),("ArchitectsDaughter","Credits"),("ArchitectsDaughter","Recenter"),
("Caveat","Northern Sky"),("Caveat","Southern Sky"),("Caveat","Starter Pack"),("Caveat","Coming soon"),("ArchitectsDaughter","A constellation may look like a W from one part of Earth and an M from another.")]
for f,s in tests: print(f,repr(s),w(f,s))
