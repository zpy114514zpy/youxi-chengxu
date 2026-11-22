import re, math

path = "Assets/Level 1.unity"
text = open(path, encoding="utf-8", errors="ignore").read()
blocks = re.split(r"^--- !u!(\d+) &(\d+)", text, flags=re.M)
docs = []
for i in range(1, len(blocks) - 1, 3):
    docs.append((blocks[i], blocks[i+1], blocks[i+2]))

def vec(body, key):
    m = re.search(key + r":\s*\{x:\s*([-\d.eE]+),\s*y:\s*([-\d.eE]+),\s*z:\s*([-\d.eE]+)", body)
    if not m:
        return None
    return tuple(float(m.group(i)) for i in (1, 2, 3))

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
        father = re.search(r"m_Father:\s*\{fileID:\s*(\d+)", body)
        tr[did] = (go, vec(body, "m_LocalPosition"), father.group(1) if father else "0")

# transform whose gameobject is named key(1)
for did, (go, pos, father) in tr.items():
    if go_name.get(go) == "key(1)":
        print("key transform", did, "local", pos)
        cur = did
        depth = 0
        while cur and cur in tr and depth < 12:
            g, p, f = tr[cur]
            print("  " * depth + "-> " + str(go_name.get(g, "?")) + "  local=" + str(tuple(round(v,3) for v in p) if p else None))
            cur = f
            depth += 1
        break
