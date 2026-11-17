exec(open("act.py").read().split("for k,v in go.items()")[0])
import sys
for k,v in objs.items():
    if v["cls"]==114 and (v["d"].get("m_Script") or {}).get("guid") in ("2cb2a15129b91d346a4bb230c9e64b9d",""):
        gid=(v["d"].get("m_GameObject") or {}).get("fileID")
        c=chain(gid)
        if "INTERACTABLES" in c: continue
        print(("MTB " if (v["d"].get("m_Script") or {}).get("guid")=="2cb2a15129b91d346a4bb230c9e64b9d" else "POKE ")+c)
