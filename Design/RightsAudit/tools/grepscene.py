exec(open("refs.py").read().split("hits={}")[0])
scene=open(os.path.join(ROOT,"Assets/Scenes/CelestialWorkshop_VRGlasses_Prototype.unity"),encoding="utf-8",errors="ignore").read()
for gn in ["Proper Motion logo","Gazebo (Blue Olive)","Pine (TreeMaker)","Starfield skybox","Ring","Realistic Hand Material","StarterAssets"]:
    gs=[g for g,(n,_) in allg.items() if n==gn]
    found=[(allg[g][1],scene.count(g)) for g in gs if g in scene]
    print("##",gn, found[:6])
