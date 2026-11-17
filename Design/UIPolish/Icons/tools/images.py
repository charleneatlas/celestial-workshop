import pickle, os, sys, json
A2=r"C:\Users\drumm\AppData\Local\Temp\claude\273560ac7b237982\a975a162-5685-4809-b47f-7281cd67214c\scratchpad\agent2-typography"
ROOT=r"C:\Users\drumm\Github\celestial-workshop"
objs, guidmap = pickle.load(open(os.path.join(A2,"scene2.pkl"),"rb"))
# extend guidmap with all metas in PackageCache (sprites)
pc=os.path.join(ROOT,"Library","PackageCache")
for dp,dn,fn in os.walk(pc):
    for f in fn:
        if f.endswith(".meta") and not f.endswith(".prefab.meta"):
            p=os.path.join(dp,f)
            try:
                with open(p,encoding="utf-8",errors="ignore") as fh:
                    for line in fh:
                        if line.startswith("guid:"):
                            guidmap.setdefault(line.split()[1], os.path.relpath(p[:-5],ROOT)); break
            except: pass
IMAGE="fe87c0e1cc204ed48ad3b37840f39efc"
go={k:v for k,v in objs.items() if v["cls"]==1}
tr={}
for k,v in objs.items():
    if v["cls"] in (4,224):
        g=(v["d"].get("m_GameObject") or {}).get("fileID")
        tr[g]=(k,v)
trid={k:v for k,v in objs.items() if v["cls"] in (4,224)}
def path(gid):
    names=[];act=True;cur=gid;n=0
    while cur and n<40:
        n+=1
        g=go.get(cur)
        if not g: names.append("?%s"%cur);break
        names.append(str(g["d"].get("m_Name")))
        if str(g["d"].get("m_IsActive","1")) in ("0","False"): act=False
        t=tr.get(cur)
        if not t: break
        f=(t[1]["d"].get("m_Father") or {}).get("fileID",0)
        if not f: break
        pt=trid.get(f)
        if not pt: names.append("?T%s"%f);break
        cur=(pt["d"].get("m_GameObject") or {}).get("fileID")
    return "/".join(reversed(names)),act
rows=[]
for k,v in objs.items():
    if v["cls"]!=114: continue
    d=v["d"]
    if (d.get("m_Script") or {}).get("guid")!=IMAGE: continue
    sp=d.get("m_Sprite") or {}
    g=sp.get("guid")
    if not g: continue
    gid=(d.get("m_GameObject") or {}).get("fileID")
    p,act=path(gid)
    t=tr.get(gid)
    size=t[1]["d"].get("m_SizeDelta") if t else None
    rows.append((p,act,str(d.get("m_Enabled")),g,guidmap.get(g,"?"),d.get("m_Color"),size,v["src"],d.get("m_Type"),d.get("m_Material")))
rows.sort()
mode=sys.argv[1] if len(sys.argv)>1 else "all"
for r in (rows if mode!="detail" else []):
    if mode=="icons" and "Icon" not in r[0].split("/")[-1] : continue
    print(("ACT " if r[1] else "INACT ")+r[0]); print("   en=%s sprite=%s %s col=%s size=%s type=%s src=%s"%(r[2],r[3],r[4],r[5],r[6],r[8],r[7]))

if mode=="detail":
    for k,v in objs.items():
        if v["cls"]!=114: continue
        d=v["d"]
        if (d.get("m_Script") or {}).get("guid")!=IMAGE: continue
        sp=d.get("m_Sprite") or {}
        if sp.get("guid")!="64f884351318b694f942625580e64a8b": continue
        gid=(d.get("m_GameObject") or {}).get("fileID")
        p,act=path(gid)
        if "INTERACTABLES" in p or p.startswith("?"): continue
        own=go[gid]["d"].get("m_IsActive")
        t=tr.get(gid)[1]["d"]
        print(p, "| fileID",sp.get("fileID"),"| ownActive",own,"| enabled",d.get("m_Enabled"),"| raycast",d.get("m_RaycastTarget"),"| preserveAspect",d.get("m_PreserveAspect"),"| mat",d.get("m_Material"),"| anchoredPos",t.get("m_AnchoredPosition"))
