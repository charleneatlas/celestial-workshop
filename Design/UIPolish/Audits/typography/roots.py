import pickle
objs,_=pickle.load(open("scene2.pkl","rb"))
for k,o in objs.items():
    if o["cls"] in (4,224):
        d=o["d"]; f=(d.get("m_Father") or {}).get("fileID",0)
        if f==0 or f not in objs:
            go=objs.get((d.get("m_GameObject") or {}).get("fileID"),{}).get("d",{})
            print(f"{'ROOT' if f==0 else 'ORPHAN'} {go.get('m_Name')!r} active={go.get('m_IsActive')} pos={d.get('m_LocalPosition')} {d.get('_prefabroot','')}")
