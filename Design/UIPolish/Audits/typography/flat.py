import re, os, sys, json, math, yaml, pickle
ROOT = r"C:\Users\drumm\Github\celestial-workshop"
OUT = os.path.dirname(os.path.abspath(__file__))
TMP3D = "9541d86e2fd84c1d9990edf0852d74ab"
TMPUI = "f4688fdb7df04437aeb418b961361dc5"

# guid map
guidmap = {}
for base in ("Assets", "Packages"):
    for dp, dn, fn in os.walk(os.path.join(ROOT, base)):
        for f in fn:
            if f.endswith(".meta"):
                p = os.path.join(dp, f)
                try:
                    with open(p, encoding="utf-8", errors="ignore") as fh:
                        for line in fh:
                            if line.startswith("guid:"):
                                guidmap[line.split()[1]] = os.path.relpath(p[:-5], ROOT); break
                except Exception: pass
# Library/PackageCache too
pc = os.path.join(ROOT, "Library", "PackageCache")
if os.path.isdir(pc):
    for dp, dn, fn in os.walk(pc):
        for f in fn:
            if f.endswith(".prefab.meta"):
                p = os.path.join(dp, f)
                with open(p, encoding="utf-8", errors="ignore") as fh:
                    for line in fh:
                        if line.startswith("guid:"):
                            guidmap.setdefault(line.split()[1], os.path.relpath(p[:-5], ROOT)); break

hdr = re.compile(r"^--- !u!(\d+) &(-?\d+)( stripped)?", re.M)
class L(yaml.CSafeLoader): pass
cache = {}
def load(path):
    if path in cache: return cache[path]
    full = path if os.path.isabs(path) else os.path.join(ROOT, path)
    txt = open(full, encoding="utf-8", errors="ignore").read()
    docs = {}
    ms = list(hdr.finditer(txt))
    for i, m in enumerate(ms):
        body = txt[m.end(): ms[i+1].start() if i+1 < len(ms) else len(txt)]
        cls, fid, stripped = int(m.group(1)), int(m.group(2)), bool(m.group(3))
        # only parse what we need
        if cls in (1, 4, 224, 1001, 223) or (cls == 114 and (TMP3D in body[:2000] or TMPUI in body[:2000] or True)):
            try:
                d = yaml.load(body, Loader=L)
            except Exception as e:
                continue
            if not d: continue
            k = list(d.keys())[0]
            docs[fid] = (cls, stripped, d[k])
    cache[path] = docs
    return docs

MASK = 0x7FFFFFFFFFFFFFFF
def derived(inst, src):
    return (inst ^ src) & MASK

def setpath(obj, path, value, objref):
    parts = path.split(".")
    cur = obj
    for p in parts[:-1]:
        if p == "Array": continue
        m = re.match(r"data\[(\d+)\]", p)
        if m:
            idx = int(m.group(1))
            if not isinstance(cur, list): return
            while len(cur) <= idx: cur.append({})
            if not isinstance(cur[idx], dict): cur[idx] = {}
            cur = cur[idx]; continue
        if not isinstance(cur, dict): return
        if p not in cur or not isinstance(cur[p], (dict, list)): cur[p] = {}
        cur = cur[p]
    last = parts[-1]
    if not isinstance(cur, dict): return
    if objref and objref.get("fileID", 0) != 0:
        cur[last] = objref
    else:
        cur[last] = value

def flatten(path, depth=0):
    """returns dict id->{cls, data} for all objects (native + nested-derived)."""
    docs = load(path)
    objs = {}
    for fid, (cls, stripped, d) in docs.items():
        if not stripped and cls != 1001:
            objs[fid] = {"cls": cls, "d": json.loads(json.dumps(d, default=str)), "src": path}
    for fid, (cls, stripped, d) in docs.items():
        if cls != 1001 or depth > 6: continue
        mod = d.get("m_Modification", {}) or {}
        sp = d.get("m_SourcePrefab", {}) or {}
        g = sp.get("guid")
        if not g or g not in guidmap: continue
        sub = flatten(guidmap[g], depth + 1)
        # apply modifications
        byid = {}
        for k, v in sub.items():
            byid[k] = {"cls": v["cls"], "d": json.loads(json.dumps(v["d"])), "src": v["src"]}
        for m in mod.get("m_Modifications", []) or []:
            t = m.get("target", {}) or {}
            tid = t.get("fileID")
            if tid in byid:
                setpath(byid[tid]["d"], m.get("propertyPath", ""), m.get("value"), m.get("objectReference"))
        removed = set()
        for r in mod.get("m_RemovedComponents", []) or []:
            removed.add(r.get("fileID"))
        for r in mod.get("m_RemovedGameObjects", []) or []:
            removed.add(r.get("fileID"))
        tparent = (mod.get("m_TransformParent") or {}).get("fileID", 0)
        for k, v in byid.items():
            if k in removed: continue
            nid = derived(fid, k)
            dd = v["d"]
            # remap references inside
            for key in ("m_GameObject", "m_Father"):
                if isinstance(dd.get(key), dict) and dd[key].get("fileID"):
                    dd[key] = {"fileID": derived(fid, dd[key]["fileID"])}
            if v["cls"] in (4, 224) and (not dd.get("m_Father") or dd["m_Father"].get("fileID", 0) == 0):
                dd["m_Father"] = {"fileID": tparent}
                dd["_prefabroot"] = guidmap[g]
            if v["cls"] == 1:
                dd["_inst"] = fid
            objs[nid] = v
    return objs

if __name__ == "__main__":
    target = sys.argv[1]
    objs = flatten(target)
    pickle.dump((objs, guidmap), open(os.path.join(OUT, sys.argv[2]), "wb"))
    print(len(objs))
