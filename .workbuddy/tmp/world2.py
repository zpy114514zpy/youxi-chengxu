import re, sys

path = "Assets/Level 1.unity"
text = open(path, encoding="utf-8", errors="ignore").read()
parts = re.split(r"^--- !u!(\d+) &(\d+)", text, flags=re.M)
docs = []
for i in range(1, len(parts) - 1, 3):
    docs.append((parts[i], parts[i + 1], parts[i + 2]))

def vec(body, key):
    m = re.search(key + r":\s*\{x:\s*([-\d.eE]+),\s*y:\s*([-\d.eE]+),\s*z:\s*([-\d.eE]+)", body)
    if not m:
        return None
    return tuple(float(m.group(i)) for i in (1, 2, 3))

# ---- plain transforms in the scene
trs = {}
for cls, did, body in docs:
    if cls != "4":
        continue
    m = re.search(r"m_GameObject:\s*\{fileID:\s*(\d+)", body)
    if not m:
        continue
    f = re.search(r"m_Father:\s*\{fileID:\s*(\d+)", body)
    trs[did] = dict(go=m.group(1), pos=vec(body, "m_LocalPosition") or (0, 0, 0),
                    rot=vec(body, "m_LocalRotation") or (0, 0, 0, 1),
                    scl=vec(body, "m_LocalScale") or (1, 1, 1),
                    father=f.group(1) if f else "0")

# ---- prefab instance roots
inst = {}
for cls, did, body in docs:
    if cls != "1001":
        continue
    par = re.search(r"m_TransformParent:\s*\{fileID:\s*(\d+)", body)
    name = None
    pos = {}
    scl = {}
    rot = {}
    for m in re.finditer(r"propertyPath:\s*(\S+)\s*\n\s*(?:value:\s*(.*))?", body):
        p = m.group(1)
        v = (m.group(2) or "").strip()
        if p == "m_Name":
            name = v
        elif p.startswith("m_LocalPosition."):
            pos[p[-1]] = v
        elif p.startswith("m_LocalScale."):
            scl[p[-1]] = v
        elif p.startswith("m_LocalRotation."):
            rot[p[-1]] = v
    if not name:
        continue
    def f3(d, dflt):
        try:
            return (float(d["x"]), float(d["y"]), float(d["z"]))
        except Exception:
            return dflt
    def f4(d):
        try:
            return (float(d["x"]), float(d["y"]), float(d["z"]), float(d["w"]))
        except Exception:
            return (0, 0, 0, 1)
    inst[did] = dict(name=name, pos=f3(pos, None), scl=f3(scl, (1, 1, 1)), rot=f4(rot),
                     father=par.group(1) if par else "0")

def qrot(q, v):
    x, y, z, w = q
    cx = y * v[2] - z * v[1]; cy = z * v[0] - x * v[2]; cz = x * v[1] - y * v[0]
    cx += w * v[0]; cy += w * v[1]; cz += w * v[2]
    rx = y * cz - z * cy; ry = z * cx - x * cz; rz = x * cy - y * cx
    return (v[0] + 2 * rx, v[1] + 2 * ry, v[2] + 2 * rz)

def qmul(a, b):
    ax, ay, az, aw = a; bx, by, bz, bw = b
    return (aw * bx + ax * bw + ay * bz - az * by,
            aw * by - ax * bz + ay * bw + az * bx,
            aw * bz + ax * by - ay * bx + az * bw,
            aw * bw - ax * bx - ay * by - az * bz)

cache = {}
def world(kind, tid, depth=0):
    key = (kind, tid)
    if key in cache:
        return cache[key]
    if depth > 30:
        return ((0, 0, 0), (0, 0, 0, 1), (1, 1, 1))
    if kind == "i":
        t = inst[tid]
        lp, lr, ls = t["pos"], t["rot"], t["scl"]
        father = t["father"]
    else:
        t = trs[tid]
        lp, lr, ls = t["pos"], t["rot"], t["scl"]
        father = t["father"]
    if father == "0":
        res = (lp, lr, ls)
    else:
        fk = "i" if father in inst else ("t" if father in trs else None)
        if fk is None:
            res = (lp, lr, ls)      # parent lives inside another prefab asset: treat as root
        else:
            pp, pr, ps = world(fk, father, depth + 1)
            p = qrot(pr, (lp[0] * ps[0], lp[1] * ps[1], lp[2] * ps[2]))
            res = ((p[0] + pp[0], p[1] + pp[1], p[2] + pp[2]), qmul(pr, lr),
                   (ps[0] * ls[0], ps[1] * ls[1], ps[2] * ls[2]))
    cache[key] = res
    return res

def chain(kind, tid):
    out = []
    cur, ck = tid, kind
    while cur and len(out) < 12:
        if ck == "i":
            out.append(inst[cur]["name"] + "[P]")
            f = inst[cur]["father"]
            ck = "i" if f in inst else ("t" if f in trs else None)
            cur = f
        elif ck == "t":
            out.append(trs[cur]["go"])
            f = trs[cur]["father"]
            ck = "i" if f in inst else ("t" if f in trs else None)
            cur = f
        else:
            break
    return out

want = sys.argv[1:]
for did, t in inst.items():
    if t["pos"] is None:
        continue
    if want and not any(w in t["name"] for w in want):
        continue
    p, r, s = world("i", did)
    print("%-34s world=(%.3f, %.3f, %.3f) scale=(%.3f, %.3f, %.3f)" % (t["name"], p[0], p[1], p[2], s[0], s[1], s[2]))
    print("      chain: " + " <- ".join(chain("i", did)[1:]))
