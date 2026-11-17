exec(open("refs.py").read().split("groups={")[0])
import sys
for k,v in go.items():
    n=v["d"].get("m_Name")
    if n in sys.argv[1:]: print(chain(k))
