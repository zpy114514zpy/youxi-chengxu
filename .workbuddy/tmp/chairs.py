import re

path = r"E:/unity-new/3D-demo/youxi-chengxu/Assets/Level 1.unity"
data = open(path, encoding="utf-8", errors="replace").read()

blocks = {}
cur = None
buf = []
for line in data.splitlines():
    m = re.match(r"^--- !u!(\d+) &(-?\d+)( stripped)?", line)
    if m:
        if cur is not None:
            blocks[cur] = "\n".join(buf)
        cur = int(m.group(2))
        buf = [line]
    else:
        buf.append(line)
if cur is not None:
    blocks[cur] = "\n".join(buf)

def ref(blk, name):
    m = re.search(r"^\s*" + re.escape(name) + r":\s*\{fileID:\s*(-?\d+)", blk, re.M)
    return int(m.group(1)) if m else None

def field(blk, name):
    m = re.search(r"^\s*" + re.escape(name) + r":\s*(.*)$", blk, re.M)
    return m.group(1).strip() if m else None

instances = {}
for oid, blk in blocks.items():
    if not blk.startswith("--- !u!1001"):
        continue
    parent = ref(blk, "m_TransformParent")
    mods = {}
    for mm in re.finditer(r"-\s*target:\s*\{fileID:\s*(-?\d+),[^}]*\}\s*\n\s*propertyPath:\s*([\w\.]+)\s*\n\s*value:\s*(.*)", blk):
        mods[(int(mm.group(1)), mm.group(2))] = mm.group(3).strip()
    src = re.search(r"m_SourcePrefab:\s*\{fileID:\s*(-?\d+),\s*guid:\s*([0-9a-f]+)", blk)
    instances[oid] = (parent, mods, src.group(2) if src else "")

def is_stripped(bid):
    return " stripped" in blocks.get(bid, "").split("\n")[0]

def local_pos(text):
    m = re.search(r"m_LocalPosition:\s*\{x:\s*(-?[\d.eE+-]+),\s*y:\s*(-?[\d.eE+-]+),\s*z:\s*(-?[\d.eE+-]+)", text)
    return [float(m.group(1)), float(m.group(2)), float(m.group(3))] if m else [0.0, 0.0, 0.0]

def world(goid, depth=0):
    if not goid or depth > 12:
        return [0.0, 0.0, 0.0]
    blk = blocks.get(goid, "")
    if is_stripped(goid):
        inst = ref(blk, "m_PrefabInstance")
        parent, mods, guid = instances.get(inst, (None, {}, ""))
        p = [0.0, 0.0, 0.0]
        for (t, prop), v in mods.items():
            if prop.startswith("m_LocalPosition."):
                p["xyz".index(prop[-1])] = float(v)
        if parent:
            pgoid = ref(blocks.get(parent, ""), "m_GameObject")
            base = world(pgoid, depth + 1) if pgoid else [0.0, 0.0, 0.0]
            return [base[i] + p[i] for i in range(3)]
        return p
    for c in re.findall(r"- component: \{fileID: (-?\d+)\}", blk):
        cb = blocks.get(int(c), "")
        if cb.startswith("--- !u!4 "):
            p = local_pos(cb)
            f = ref(cb, "m_Father")
            if f:
                fg = ref(blocks.get(f, ""), "m_GameObject")
                base = world(fg, depth + 1) if fg else [0.0, 0.0, 0.0]
                return [base[i] + p[i] for i in range(3)]
            return p
    return [0.0, 0.0, 0.0]

def iname(oid):
    parent, mods, guid = instances.get(oid, (None, {}, ""))
    nm = next((v for (t, p), v in mods.items() if p == "m_Name"), "")
    if nm:
        return nm
    # 场景里被剥掉名字时，退回看源预制体名
    src = re.search(r"m_SourcePrefab:\s*\{fileID:\s*(-?\d+),\s*guid:\s*([0-9a-f]+)", blocks.get(oid, ""))
    return "guid:" + (src.group(2) if src else "?")

print("=== 名字里带 Chair / Stool 的预制体实例 ===")
for oid in instances:
    nm = iname(oid)
    if not re.search(r"chair|stool|seat", nm, re.I):
        continue
    parent, mods, guid = instances[oid]
    p = [0.0, 0.0, 0.0]
    for (t, prop), v in mods.items():
        if prop.startswith("m_LocalPosition."):
            p["xyz".index(prop[-1])] = float(v)
    base = [0.0, 0.0, 0.0]
    if parent:
        pgoid = ref(blocks.get(parent, ""), "m_GameObject")
        base = world(pgoid) if pgoid else [0.0, 0.0, 0.0]
    w = [base[i] + p[i] for i in range(3)]
    if not (-26 < w[0] < -14 and -97 < w[2] < -88):
        tag = ""
    else:
        tag = "   <== 卧室/一号房间"
    print("  %-46s (%7.2f,%6.2f,%8.2f)%s" % (nm[:46], w[0], w[1], w[2], tag))
