# -*- coding: utf-8 -*-
import re, math, io

path = "Assets/Level 1.unity"
text = io.open(path, encoding="utf-8", errors="ignore").read()
blocks = re.split(r"^--- !u!(\d+) &(\d+)", text, flags=re.M)
docs = []
for i in range(1, len(blocks) - 1, 3):
    docs.append((blocks[i], blocks[i+1], blocks[i+2]))

KEYS = {
    "相册(-21.35,3.21,-102.30)": (-21.35, 3.212, -102.30),
    "茶杯/沙发(-20.0,3.4,-103.5)": (-20.0, 3.4, -103.5),
}

for label, KEY in KEYS.items():
    print("=" * 70)
    print(label)
    res = []
    for cls, did, body in docs:
        if cls != "1001":
            continue
        mods = re.findall(r"propertyPath:\s*([^\n]+)\n\s*value:\s*([^\n]*)", body)
        name = None
        pos = {}
        for p, v in mods:
            p = p.strip(); v = v.strip()
            if p == "m_Name":
                name = v
            if p.startswith("m_LocalPosition."):
                pos[p.split(".")[-1]] = v
        if not name or len(pos) < 3:
            continue
        try:
            pp = (float(pos["x"]), float(pos["y"]), float(pos["z"]))
        except Exception:
            continue
        d = math.dist(pp, KEY)
        if d < 4.0:
            res.append((d, name, pp))
    res.sort()
    for d, name, pp in res[:30]:
        print("%.2f  %-40s %s" % (d, name, tuple(round(v, 2) for v in pp)))
