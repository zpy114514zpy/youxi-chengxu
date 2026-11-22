import re, sys

def load(path):
    text = open(path, encoding="utf-8", errors="ignore").read()
    parts = re.split(r"^--- !u!(\d+) &(\d+)", text, flags=re.M)
    docs = []
    for i in range(1, len(parts) - 1, 3):
        docs.append((parts[i], parts[i + 1], parts[i + 2]))
    return docs

def vec(body, key):
    m = re.search(key + r":\s*\{x:\s*([-\d.eE]+),\s*y:\s*([-\d.eE]+),\s*z:\s*([-\d.eE]+)", body)
    if not m:
        return None
    return tuple(float(m.group(i)) for i in (1, 2, 3))

for path in sys.argv[1:]:
    print("=====", path)
    docs = load(path)
    names = {}
    for cls, did, body in docs:
        if cls == "1":
            m = re.search(r"m_Name:\s*(.*)", body)
            if m:
                names[did] = m.group(1).strip()
    trs = {}
    for cls, did, body in docs:
        if cls == "4":
            m = re.search(r"m_GameObject:\s*\{fileID:\s*(\d+)", body)
            if not m:
                continue
            f = re.search(r"m_Father:\s*\{fileID:\s*(\d+)", body)
            trs[did] = (m.group(1), vec(body, "m_LocalPosition"), vec(body, "m_LocalScale"),
                        vec(body, "m_LocalRotation"), f.group(1) if f else "0")
    # meshes on renderers
    mesh_of = {}
    for cls, did, body in docs:
        if cls == "23":  # MeshRenderer
            m = re.search(r"m_GameObject:\s*\{fileID:\s*(\d+)", body)
            mf = re.search(r"m_Mesh:\s*\{fileID:\s*(\d+)", body)
            if m and mf:
                mesh_of[m.group(1)] = mf.group(1)
    for did, (go, pos, scl, rot, father) in trs.items():
        print("%-40s pos=%s scale=%s father=%s" % (names.get(go, "?"), pos, scl, father))
