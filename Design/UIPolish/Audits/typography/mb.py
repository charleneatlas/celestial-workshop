import pickle,sys
objs,gm=pickle.load(open("scene.pkl","rb"))
rev={v.replace("\\","/"):k for k,v in gm.items()}
for name in sys.argv[1:]:
    g=[k for p,k in rev.items() if p.endswith("/"+name+".cs")]
    for k,o in objs.items():
        if o["cls"]==114 and (o["d"].get("m_Script") or {}).get("guid") in g:
            go=objs.get((o["d"].get("m_GameObject") or {}).get("fileID"),{}).get("d",{})
            print("==",name,go.get("m_Name"))
            for kk,vv in o["d"].items():
                if kk.startswith("m_") : continue
                if isinstance(vv,dict) and "fileID" in vv: continue
                s=str(vv)
                print("  ",kk,"=",s[:160])
