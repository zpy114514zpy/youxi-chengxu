import re, sys, math

path = "Assets/Level 1.unity"
text = open(path, encoding="utf-8", errors="ignore").read()
parts = re.split(r"^--- !u!(\d+) &(\d+)", text, flags=re.M)
docs = []
for i in range(1, len(parts) - 1, 3):
    docs.append((parts[i], parts[i + 1], parts[i + 2]))

def vec(body, key):
    m = re.search(key + r":\s*\{x:\s*([-\d.eE]+),\s*y:\s*([-\d.eE]+),\s*z:\s*([-\d.eE]+)", body)
    if not m:
        return (0.0, 0.0, 0.0)
    return tuple(float(m.group(i)) for i in (1, 2, 3))

go_name = {}      # gameobject fileID -> name (from real class 1 blocks)
go_prefab = {}    # gameobject fileID -> prefab instance fileID
for cls, did, body in docs:
    if cls == "1":
        m = re.search(r"m_Name:\s*(.*)", body)
        if m:
            go_name[did] = m.group(1).strip()
        p = re.search(r"m_PrefabInstance:\s*\{fileID:\s*(\d+)", body)
        if p:
            go_prefab[did] = p.group(1)

# names overridden through prefab modifications
prefab_name = {}
for cls, did, body in docs:
    if cls == "1001":
        for m in re.finditer(r"propertyPath:\s*(\S+)\s*\n\s*value:\s*(.*)", body):
            if m.group(1) == "m_Name":
                prefab_name[did] = m.group(2).strip()

trs = {}
for cls, did, body in docs:
    if cls != "4":
        continue
    m = re.search(r"m_GameObject:\s*\{fileID:\s*(\d+)", body)
    if not m:
        continue
    go = m.group(1)
    f = re.search(r"m_Father:\s*\{fileID:\s*(\d+)", body)
    trs[did] = dict(go=go, pos=vec(body, "m_LocalPosition"),
                    rot=vec(body, "m_LocalRotation"), scl=vec(body, "m_LocalScale"),
                    father=f.group(1) if f else "0")

def name_of(go):
    if go in go_name:
        return go_name[go]
    if go in go_prefab and go_prefab[go] in prefab_name:
        return prefab_name[go_prefab[go]]
    return "?"

def qrot(q, v):
    x, y, z, w = q
    # v' = v + 2*cross(q.xyz, cross(q.xyz, v) + w*v)
    cx = y * v[2] - z * v[1]
    cy = z * v[0] - x * v[2]
    cz = x * v[1] - y * v[0]
    cx += w * v[0]; cy += w * v[1]; cz += w * v[2]
    rx = y * cz - z * cy
    ry = z * cx - x * cz
    rz = x * cy - y * cx
    return (v[0] + 2 * rx, v[1] + 2 * ry, v[2] + 2 * rz)

cache = {}
def world(tid, depth=0):
    if tid in cache:
        return cache[tid]
    t = trs.get(tid)
    if t is None or depth > 40:
        return ((0, 0, 0), (0, 0, 0, 1), (1, 1, 1))
    if t["father"] == "0" or t["father"] not in trs:
        res = (t["pos"], t["rot"], t["scl"])
    else:
        pp, pr, ps = world(t["father"], depth + 1)
        p = qrot(pr, (t["pos"][0] * ps[0], t["pos"][1] * ps[1], t["pos"][2] * ps[2]))
        p = (p[0] + pp[0], p[1] + pp[1], p[2] + pp[2])
        # quaternion multiply pr * t.rot
        ax, ay, az, aw = pr
        bx, by, bz, bw = t["rot"]
        rot = (aw * bx + ax * bw + ay * bz - az * by,
               aw * by - ax * bz + ay * bw + az * bx,
               aw * bz + ax * by - ay * bx + az * bw,
               aw * bw - ax * bx - ay * by - az * bz)
        scl = (ps[0] * t["scl"][0], ps[1] * t["scl"][1], ps[2] * t["scl"][2])
        res = (p, rot, scl)
    cache[tid] = res
    return res

targets = sys.argv[1:] or ["key(1)"]
for tid, t in trs.items():
    nm = name_of(t["go"])
    if nm not in targets:
        continue
    p, r, s = world(tid)
    chain = []
    cur = tid
    while cur in trs and len(chain) < 10:
        chain.append(name_of(trs[cur]["go"]))
        cur = trs[cur]["father"]
    if len(chain) > 1:
        chain = chain[1:]
    print("%-32s world=(%.3f, %.3f, %.3f) scale=(%.3f, %.3f, %.3f)" % (nm, p[0], p[1], p[2], s[0], s[1], s[2]))
    print("      chain: " + " <- ".join(chain))
