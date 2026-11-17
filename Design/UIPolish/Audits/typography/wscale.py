import sys
sys.argv=[sys.argv[0],"scene.pkl"]
exec(open("analyze.py").read().split("rows = []")[0])
names=set(["Star Chart Blueprint","Guild Certificate","Workshop Noticeboard","Field Note Reader","Constellation Boxes","Workstation","Post-Puzzle Menu","Puzzle Back Button","Boxes Back To Title Button"])
for go_id,t in tr_by_go.items():
    n=objs.get(go_id,{}).get("d",{}).get("m_Name")
    if n in names:
        M,chain=world(t); S=np.linalg.norm(M[:3,:3],axis=0)
        print(n,S.round(4),M[:3,3].round(3), "/".join(c for c,_ in chain))
