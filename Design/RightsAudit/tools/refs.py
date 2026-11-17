import os,sys,json,re,glob
A4=r"C:\Users\drumm\AppData\Local\Temp\claude\273560ac7b237982\a975a162-5685-4809-b47f-7281cd67214c\scratchpad\agent4-icons\tools"
os.chdir(A4)
src=open("act.py").read().split("for k,v in go.items()")[0]
exec(src)
ROOT=r"C:\Users\drumm\Github\celestial-workshop"
def guid(p):
    for l in open(os.path.join(ROOT,p+".meta"),encoding="utf-8",errors="ignore"):
        if l.startswith("guid:"): return l.split()[1]
groups={
 "ChatGPT/ai/California/Camera textures":["Assets/Textures"],
 "Proper Motion logo":["Assets/Textures/CW_Textures/Proper Motion Logo"],
 "Gazebo (Blue Olive)":["Assets/ThirdParty/Blue Olive Studio"],
 "Pine (TreeMaker)":["Assets/ThirdParty/TreeMaker"],
 "Starfield skybox":["Assets/ThirdParty/ParallelCascades"],
 "Ring":["Assets/Ring"],
 "Realistic Hand Material":["Assets/Realistic Hand Material"],
 "StarterAssets":["Assets/StarterAssets"],
 "ShowcaseSamples":["Assets/ShowcaseSamples"],
 "Audio root/anvil":["Assets/Audio/interaction_hammer_hit_anvil.wav"],
 "Pixabay-named SFX":["Assets/Audio/CW_Audio/dragon-studio-holy-spell-cast-450460.wav","Assets/Audio/CW_Audio/universfield-magic-teleport-whoosh-352764_SHORTER.wav","Assets/Audio/CW_Audio/constellation_dropping_on_table.wav"],
 "ISDK sound copies":["Assets/Audio/CW_Audio/ISDK"],
 "Music":["Assets/Audio/Music"],
}
def files(p):
    full=os.path.join(ROOT,p)
    if os.path.isfile(full): return [p]
    out=[]
    for dp,dn,fn in os.walk(full):
        if "CW_Textures" in dp and "Proper" not in p: continue
        for f in fn:
            if not f.endswith(".meta"): out.append(os.path.relpath(os.path.join(dp,f),ROOT))
    return out
gmap={}
for gname,paths in groups.items():
    for p in paths:
        for f in files(p):
            g=guid(f)
            if g: gmap[g]=(gname,f)
# second level: materials/prefabs anywhere in Assets referencing these guids
extra={}
for dp,dn,fn in os.walk(os.path.join(ROOT,"Assets")):
    for f in fn:
        if f.endswith((".mat",".prefab",".asset")) and not f.endswith(".meta"):
            fp=os.path.join(dp,f)
            try: t=open(fp,encoding="utf-8",errors="ignore").read()
            except: continue
            for g,(gn,src) in gmap.items():
                if g in t:
                    rel=os.path.relpath(fp,ROOT); gg=guid(rel)
                    if gg and gg not in gmap: extra[gg]=(gn,rel+" -> "+src)
allg=dict(gmap); allg.update(extra)
hits={}
pat=re.compile(r"[0-9a-f]{32}")
for k,v in objs.items():
    s=json.dumps(v["d"])
    found=set(pat.findall(s))&set(allg)
    if not found: continue
    gid=(v["d"].get("m_GameObject") or {}).get("fileID")
    c=chain(gid) if gid else "(non-GO obj cls %s)"%v["cls"]
    for g in found:
        hits.setdefault(allg[g][0],set()).add((c.split("/")[0]+("" if "[OFF]" not in c else " [inactive somewhere]"), allg[g][1].split(" -> ")[-1].split("/")[-1]))
for gn in groups:
    h=hits.get(gn,set())
    print("##",gn,len(h))
    for x in sorted(h)[:12]: print("   ",x)
