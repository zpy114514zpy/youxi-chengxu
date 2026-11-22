import re, sys, math

path = "Assets/Level 1.unity"
text = open(path, encoding="utf-8", errors="ignore").read()
blocks = re.split(r"^--- !u!(\d+) &(\d+)", text, flags=re.M)
# blocks: [pre, cls, id, body, cls, id, body...]
docs = []
for i in range(1, len(blocks) - 1, 3):
    docs.append((blocks[i], blocks[i+1], blocks[i+2]))

def vec(body, key):
    m = re.search(key + r":\s*\{x:\s*([-\d.eE]+),\s*y:\s*([-\d.eE]+),\s*z:\s*([-\d.eE]+)", body)
    if not m:
        return None
    return tuple(float(m.group(i)) for i in (1, 2, 3))

KEY = (-23.932, 3.462, -98.867)

# collect transforms: GameObject(1) has m_Component list; Transform(4) has m_GameObject, m_LocalPosition, m_Father
go_name = {}
for cls, did, body in docs:
    if cls == "1":
        m = re.search(r"m_Name:\s*(.*)", body)
        if m:
            go_name[did] = m.group(1).strip()

tr = {}
for cls, did, body in docs:
    if cls == "4":
        m = re.search(r"m_GameObject:\s*\{fileID:\s*(\d+)", body)
        if not m:
            continue
        go = m.group(1)
        pos = vec(body, "m_LocalPosition")
        father = re.search(r"m_Father:\s*\{fileID:\s*(\d+)", body)
        tr[did] = (go, pos, father.group(1) if father else "0")

# world position approx: only if parents are identity-ish. Just report locals of roots near key.
out = []
for did, (go, pos, father) in tr.items():
    if pos is None:
        continue
    d = math.dist(pos, KEY)
    if d < 5.0:
        out.append((d, go_name.get(go, "?"), pos, father))
out.sort()
for d, name, pos, father in out[:60]:
    print("%.2f  %-45s %s  father=%s" % (d, name, tuple(round(v,3) for v in pos), father))
