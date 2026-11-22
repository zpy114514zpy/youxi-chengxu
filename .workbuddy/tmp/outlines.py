# -*- coding: utf-8 -*-
import re, io, sys

path = "Assets/Level 1.unity"
text = io.open(path, encoding="utf-8", errors="ignore").read()
blocks = re.split(r"^--- !u!(\d+) &(\d+)[^\n]*$", text, flags=re.M)
# blocks: [pre, cls, id, body, cls, id, body, ...]
cls, oid, body = {}, {}, {}
i = 1
while i + 2 < len(blocks) + 1 and i + 2 <= len(blocks):
    c = blocks[i]; o = blocks[i+1]; b = blocks[i+2]
    cls[o] = int(c); oid[o] = o; body[o] = b
    i += 3

GUID = "2d3a12e3744a56045859c696975566a4"

# gameobject -> name
name_of = {}
for o, b in body.items():
    if cls[o] != 1: continue
    m = re.search(r"m_Name: (.*)", b)
    if m: name_of[o] = m.group(1).strip()

# component -> gameobject
go_of = {}
for o, b in body.items():
    if cls[o] in (1,): continue
    m = re.search(r"m_GameObject: \{fileID: (\d+)\}", b)
    if m: go_of[o] = m.group(1)

# transform -> position/parent
def vec(b, key):
    m = re.search(key + r":\s*\{x: ([-\d.eE]+), y: ([-\d.eE]+), z: ([-\d.eE]+)\}", b)
    if not m: return None
    return tuple(float(m.group(k)) for k in (1,2,3))

transform_pos = {}
parent_of = {}
for o, b in body.items():
    if cls[o] not in (4, 224): continue
    p = vec(b, "m_LocalPosition")
    transform_pos[o] = p
    m = re.search(r"m_Father: \{fileID: (\d+)\}", b)
    parent_of[o] = m.group(1) if m else "0"

# find transform of gameobject
tr_of_go = {}
for o, b in body.items():
    if cls[o] not in (4, 224): continue
    m = re.search(r"m_GameObject: \{fileID: (\d+)\}", b)
    if m: tr_of_go[m.group(1)] = o

for o, b in body.items():
    if GUID not in b: continue
    go = go_of.get(o, "?")
    tr = tr_of_go.get(go)
    print("组件", o, "->", name_of.get(go, "?"), "go=", go, "local pos=", transform_pos.get(tr))
    print("   父:", name_of.get(go_of.get(parent_of.get(tr, "0"), ""), parent_of.get(tr)))
    for line in b.splitlines():
        if "outline" in line.lower() or "reveal" in line.lower() or "Distance" in line:
            print("   ", line.strip())
