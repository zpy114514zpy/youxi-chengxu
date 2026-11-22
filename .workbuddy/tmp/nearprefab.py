import re, math

path = "Assets/Level 1.unity"
text = open(path, encoding="utf-8", errors="ignore").read()
blocks = re.split(r"^--- !u!(\d+) &(\d+)", text, flags=re.M)
docs = []
for i in range(1, len(blocks) - 1, 3):
    docs.append((blocks[i], blocks[i+1], blocks[i+2]))

KEY = (-23.932, 3.4621, -98.8673)

# prefab instances (class 1001)
results = []
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
        p = (float(pos["x"]), float(pos["y"]), float(pos["z"]))
    except Exception:
        continue
    d = math.dist(p, KEY)
    if d < 5.0:
        results.append((d, name, p))

results.sort()
for d, name, p in results[:40]:
    print("%.2f  %-40s %s" % (d, name, tuple(round(v, 2) for v in p)))
