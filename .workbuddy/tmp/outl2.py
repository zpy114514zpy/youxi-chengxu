# -*- coding: utf-8 -*-
import re, io

path = "Assets/Level 1.unity"
text = io.open(path, encoding="utf-8", errors="ignore").read()
parts = re.split(r"^--- !u!(\d+) &(\d+)[^\n]*$", text, flags=re.M)
blocks = []
i = 1
while i + 2 <= len(parts):
    blocks.append((int(parts[i]), parts[i+1], parts[i+2]))
    i += 3

by_id = {b[1]: b for b in blocks}
name_of = {}
for c, o, b in blocks:
    if c == 1:
        m = re.search(r"m_Name: (.*)", b)
        if m: name_of[o] = m.group(1).strip()

targets = ["469987008", "567590319", "1076844299", "1160989947"]
for t in targets:
    print("="*60)
    for c, o, b in blocks:
        if c == 1 and o == t:
            print("GO id", o, "name=", name_of.get(o))
            print(b[:600])
    # component list
    for c, o, b in blocks:
        if c == 1 or by_id.get(o) is None: continue
        if "m_GameObject: {fileID: " + t + "}" in b:
            print("  comp", c, o, b.splitlines()[0:3])
