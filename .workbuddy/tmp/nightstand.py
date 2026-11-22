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
    if blk.startswith("--- !u!1001"):
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

def go_name(goid):
    blk = blocks.get(goid, "")
    if is_stripped(goid):
        inst = ref(blk, "m_PrefabInstance")
        mods = instances.get(inst, (None, {}, ""))[1]
        return next((v for (t, p), v in mods.items() if p == "m_Name"), "inst")
    return field(blk, "m_Name")

CAB_GUID = "dac1a000000005080821635273821879"
BEDT_GUID = "dac1a000000003999727830334759986"
CABPREFAB = "dac1a000000007198505170147166920"

print("=== MeshRenderer using SM_BedTable_A1_Cab mesh ===")
for oid, blk in blocks.items():
    if not blk.startswith("--- !u!23 "):
        continue
    if CAB_GUID not in blk:
        continue
    goid = ref(blk, "m_GameObject")
    pos = world(goid)
    print("  cab mesh go=%-12s (%7.2f,%7.2f,%8.2f)  name=%s" % (goid, pos[0], pos[1], pos[2], go_name(goid)))

print("=== PrefabInstance of SM_BedTable_A1_Cab ===")
for oid, (parent, mods, guid) in instances.items():
    if guid != CABPREFAB:
        continue
    p = [0.0, 0.0, 0.0]
    name = "?"
    for (t, prop), v in mods.items():
        if prop.startswith("m_LocalPosition."):
            p["xyz".index(prop[-1])] = float(v)
        if prop == "m_Name":
            name = v
    base = [0.0, 0.0, 0.0]
    if parent:
        pgoid = ref(blocks.get(parent, ""), "m_GameObject")
        base = world(pgoid) if pgoid else [0.0, 0.0, 0.0]
    print("  inst=%-12s name=%-20s local=(%6.2f,%6.2f,%7.2f) world=(%7.2f,%7.2f,%8.2f)" % (
        oid, name, p[0], p[1], p[2], base[0] + p[0], base[1] + p[1], base[2] + p[2]))

print("=== PrefabInstance of SM_BedTable_A1 (body) ===")
for oid, (parent, mods, guid) in instances.items():
    if guid != "dac1a000000006835935251056426287":
        continue
    p = [0.0, 0.0, 0.0]
    for (t, prop), v in mods.items():
        if prop.startswith("m_LocalPosition."):
            p["xyz".index(prop[-1])] = float(v)
    base = [0.0, 0.0, 0.0]
    if parent:
        pgoid = ref(blocks.get(parent, ""), "m_GameObject")
        base = world(pgoid) if pgoid else [0.0, 0.0, 0.0]
    print("  inst=%-12s local=(%6.2f,%6.2f,%7.2f) world=(%7.2f,%7.2f,%8.2f)" % (
        oid, p[0], p[1], p[2], base[0] + p[0], base[1] + p[1], base[2] + p[2]))
