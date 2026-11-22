import re, sys

def load(path):
    d = open(path, encoding='utf-8', errors='replace').read()
    parts = re.split(r"^--- !u!(\d+) &(-?\d+).*$", d, flags=re.M)
    blocks = {}
    for i in range(1, len(parts), 3):
        blocks[int(parts[i+1])] = (parts[i], parts[i+2])
    return blocks

def f(body, name):
    m = re.search(r"^\s*%s\s*:\s*(.*)$" % name, body, flags=re.M)
    return m.group(1).strip() if m else None

def v3(s):
    if not s: return (0.0, 0.0, 0.0)
    out = []
    for k in "xyz":
        m = re.search(r"[ ,{]" + k + r":\s*(-?[0-9.eE+-]+)", s)
        out.append(float(m.group(1)) if m else 0.0)
    return tuple(out)

def asm(go, blocks):
    cls, body = blocks[go]
    out = {"name": f(body, "m_Name"), "tr": None, "col": None, "size": None, "center": None,
           "children": []}
    for c in re.findall(r"component: \{fileID: (-?\d+)\}", body):
        c = int(c)
        if c not in blocks: continue
        ccls, cbody = blocks[c]
        if ccls == "4":
            out["tr"] = cbody
        elif ccls == "65":
            sz = re.search(r"m_Size: (\{[^}]*\})", cbody)
            ce = re.search(r"m_Center: (\{[^}]*\})", cbody)
            out["col"] = cbody
            out["size"] = v3(sz.group(1)) if sz else None
            out["center"] = v3(ce.group(1)) if ce else None
    if out["tr"]:
        ch = f(out["tr"], "m_Children") or ""
        for c in re.findall(r"-\s*\{fileID: (-?\d+)\}", ch):
            out["children"].append(int(c))
        out["pos"] = v3(f(out["tr"], "m_LocalPosition"))
        out["scale"] = v3(f(out["tr"], "m_LocalScale"))
    return out

def dump(go, blocks, depth=0, maxd=6):
    n = asm(go, blocks)
    print("%s%-32s pos=(%6.3f,%6.3f,%6.3f) scale=(%4.2f,%4.2f,%4.2f) col=%s%s%s" % (
        "  " * depth, str(n["name"]), n.get("pos", (0, 0, 0))[0], n.get("pos", (0, 0, 0))[1],
        n.get("pos", (0, 0, 0))[2], n.get("scale", (1, 1, 1))[0], n.get("scale", (1, 1, 1))[1],
        n.get("scale", (1, 1, 1))[2], "YES" if n["col"] else "-",
        (" size=(%.3f,%.3f,%.3f)" % n["size"]) if n["size"] else "",
        (" center=(%.3f,%.3f,%.3f)" % n["center"]) if n["center"] else ""))
    if depth >= maxd: return
    for c in n["children"]:
        if c in blocks:
            dump(c, blocks, depth + 1, maxd)

for path in sys.argv[1:]:
    print("=" * 100)
    print(path)
    b = load(path)
    for k, (cls, body) in b.items():
        if cls != "4": continue
        fa = f(body, "m_Father")
        fid = int(re.search(r"fileID: (-?\d+)", fa).group(1)) if fa else 0
        if fid != 0: continue
        go = int(re.search(r"fileID: (-?\d+)", f(body, "m_GameObject")).group(1))
        if go in b:
            dump(go, b)
