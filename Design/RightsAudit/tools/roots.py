exec(open("refs.py").read().split("groups={")[0])
import collections
c=collections.Counter()
for k,v in objs.items():
    pr=v["d"].get("_prefabroot")
    if pr and any(s in pr for s in ("Gazebo","Pine","Ring","StarterAssets","Player Character","ShowcaseSamples","Hand","Proper")):
        gid=(v["d"].get("m_GameObject") or {}).get("fileID")
        ch=chain(gid)
        c[(pr.split("\\")[-1], "/".join(ch.split("/")[:3]), "[OFF]" in ch)]+=1
for k,n in sorted(c.items()): print(n,k)
# realistic hand material users
g="%s"%open(os.path.join(ROOT,"Assets/Realistic Hand Material/M_BasicHand_Light.mat.meta")).read().split("guid: ")[1].split()[0]
for k,v in objs.items():
    if g in json.dumps(v["d"]):
        gid=(v["d"].get("m_GameObject") or {}).get("fileID"); print("HANDMAT", chain(gid) if gid else v["cls"])
