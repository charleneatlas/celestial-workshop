exec(open("images.py").read().split("rows=[]")[0])
def chain(gid):
    out=[];cur=gid
    while cur:
        g=go.get(cur)
        if not g: break
        out.append(("" if str(g["d"].get("m_IsActive","1"))=="1" else "[OFF]")+str(g["d"].get("m_Name")))
        t=tr.get(cur)
        f=(t[1]["d"].get("m_Father") or {}).get("fileID",0) if t else 0
        pt=trid.get(f)
        cur=(pt["d"].get("m_GameObject") or {}).get("fileID") if pt else None
    return "/".join(reversed(out))
for k,v in go.items():
    if v["d"].get("m_Name")=="Icon":
        c=chain(k)
        if "INTERACTABLES" in c or "ISDK" in c: continue
        print(c)
