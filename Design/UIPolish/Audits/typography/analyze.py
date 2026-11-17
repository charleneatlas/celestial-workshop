import pickle, os, sys, math
import numpy as np
OUT = os.path.dirname(os.path.abspath(__file__))
objs, guidmap = pickle.load(open(os.path.join(OUT, sys.argv[1]), "rb"))
TMP3D = "9541d86e2fd84c1d9990edf0852d74ab"; TMPUI = "f4688fdb7df04437aeb418b961361dc5"
FONT = {"7a3e683d64ebc214aaf1e712fa292158": ("ArchDaughter", 90, 65, 43),
        "943acece6cb2b5b4e9efda708b5082fb": ("Caveat", 90, 64, 33),
        "8433b450e5a14dd4f89a9be44c8203b1": ("Meddon", 90, 98, 36)}
def f(x, d=0.0):
    try: return float(x)
    except: return d
def v3(d, k, dflt=(0,0,0)):
    x = d.get(k) or {}
    return np.array([f(x.get("x"), dflt[0]), f(x.get("y"), dflt[1]), f(x.get("z"), dflt[2])])
def quat(d):
    q = d.get("m_LocalRotation") or {}
    return [f(q.get("x")), f(q.get("y")), f(q.get("z")), f(q.get("w"), 1)]
def qmat(q):
    x,y,z,w = q
    return np.array([[1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)],
                     [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)],
                     [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]])
tr_by_go = {}
for k, o in objs.items():
    if o["cls"] in (4, 224):
        go = (o["d"].get("m_GameObject") or {}).get("fileID")
        tr_by_go[go] = k
def local(k):
    d = objs[k]["d"]
    M = np.eye(4)
    M[:3,:3] = qmat(quat(d)) @ np.diag(v3(d, "m_LocalScale", (1,1,1)))
    M[:3,3] = v3(d, "m_LocalPosition")
    return M
memo = {}
def world(k, depth=0):
    if k in memo: return memo[k]
    if k not in objs or depth > 60: return np.eye(4), []
    d = objs[k]["d"]
    par = (d.get("m_Father") or {}).get("fileID", 0)
    PM, chain = (world(par, depth+1) if par else (np.eye(4), []))
    go = objs.get((d.get("m_GameObject") or {}).get("fileID"), {}).get("d", {})
    M = PM @ local(k)
    res = (M, chain + [(go.get("m_Name", "?"), str(go.get("m_IsActive", "1")))])
    memo[k] = res
    return res
rows = []
for k, o in objs.items():
    if o["cls"] != 114: continue
    d = o["d"]; g = (d.get("m_Script") or {}).get("guid")
    if g not in (TMP3D, TMPUI): continue
    if str(d.get("m_Enabled", 1)) == "0": en = "compOFF"
    else: en = ""
    gid = (d.get("m_GameObject") or {}).get("fileID")
    tk = tr_by_go.get(gid)
    if tk is None: continue
    M, chain = world(tk)
    S = np.linalg.norm(M[:3,:3], axis=0)
    fg = (d.get("m_fontAsset") or {}).get("guid")
    fn = FONT.get(fg, (guidmap.get(fg, str(fg)), 90, 70, 50))
    fs = f(d.get("m_fontSize")); amin = f(d.get("m_fontSizeMin")); amax = f(d.get("m_fontSizeMax"))
    auto = str(d.get("m_enableAutoSizing")) == "1"
    unit = 0.1 if g == TMP3D else 1.0
    sc = S[1]
    cap = lambda size: size * fn[2] / fn[1] * unit * sc * 1000  # mm
    xh = lambda size: size * fn[3] / fn[1] * unit * sc * 1000
    td = objs[tk]["d"]; sd = td.get("m_SizeDelta") or {}
    col = d.get("m_fontColor") or {}
    inactive = any(a == "0" for _, a in chain)
    mat = (d.get("m_sharedMaterial") or {})
    rows.append(dict(path="/".join(n for n, _ in chain), kind="3D" if g == TMP3D else "UI", font=fn[0],
        mat=guidmap.get(mat.get("guid"), mat.get("fileID")), text=str(d.get("m_text", ""))[:70].replace("\n", "\\n"),
        fs=fs, auto=auto, amin=amin, amax=amax, cap=cap(fs), capmin=cap(amin) if auto else None, xh=xh(fs),
        pos=M[:3,3].round(3).tolist(), scale=round(sc,5), rect=(f(sd.get("x")), f(sd.get("y"))),
        fwd=(M[:3,:3] @ np.array([0,0,1]) / max(S[2],1e-9)).round(2).tolist(),
        color=(round(f(col.get("r")),2), round(f(col.get("g")),2), round(f(col.get("b")),2), round(f(col.get("a")),2)),
        wrap=d.get("m_TextWrappingMode", d.get("m_enableWordWrapping")), ovf=d.get("m_overflowMode"),
        style=d.get("m_fontStyle"), halign=d.get("m_HorizontalAlignment"), cs=d.get("m_characterSpacing"), ls=d.get("m_lineSpacing"),
        inactive=inactive, en=en, prefab=objs[tk]["src"]))
rows.sort(key=lambda r: r["path"])
for r in rows:
    print(f"{'INACT ' if r['inactive'] else ''}{r['en']}{r['kind']} {r['path']}\n    '{r['text']}' font={r['font']} mat={r['mat']} fs={r['fs']} auto={r['auto']}[{r['amin']}-{r['amax']}] scale={r['scale']} rect={r['rect']}"
          f" cap={r['cap']:.1f}mm capmin={r['capmin'] and round(r['capmin'],1)} xh={r['xh']:.1f}mm pos={r['pos']} fwd={r['fwd']} col={r['color']} wrap={r['wrap']} ovf={r['ovf']} style={r['style']} cs={r['cs']} src={r['prefab']}")
print(len(rows))
