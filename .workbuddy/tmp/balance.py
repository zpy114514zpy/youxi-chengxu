import sys, re

def strip(src):
    out = []
    i = 0
    n = len(src)
    mode = None  # None, 'line', 'block', 'str', 'verb'
    while i < n:
        c = src[i]
        nxt = src[i + 1] if i + 1 < n else ""
        if mode is None:
            if c == "/" and nxt == "/":
                mode = "line"; i += 2; continue
            if c == "/" and nxt == "*":
                mode = "block"; i += 2; continue
            if c == '"':
                mode = "str"; i += 1; continue
            if c == "'":
                mode = "char"; i += 1; continue
            if c == "@" and nxt == '"':
                mode = "verb"; i += 2; continue
            out.append(c); i += 1; continue
        if mode == "line":
            if c == "\n":
                mode = None; out.append(c)
            i += 1; continue
        if mode == "block":
            if c == "*" and nxt == "/":
                mode = None; i += 2; continue
            i += 1; continue
        if mode == "str":
            if c == "\\":
                i += 2; continue
            if c == '"':
                mode = None
            i += 1; continue
        if mode == "char":
            if c == "\\":
                i += 2; continue
            if c == "'":
                mode = None
            i += 1; continue
        if mode == "verb":
            if c == '"' and nxt == '"':
                i += 2; continue
            if c == '"':
                mode = None
            i += 1; continue
    return "".join(out)

for path in sys.argv[1:]:
    src = open(path, encoding="utf-8-sig", errors="replace").read()
    clean = strip(src)
    pairs = {"{": "}", "(": ")", "[": "]"}
    rev = {v: k for k, v in pairs.items()}
    stack = []
    ok = True
    for idx, ch in enumerate(clean):
        if ch in pairs:
            stack.append((ch, idx))
        elif ch in rev:
            if not stack or stack[-1][0] != rev[ch]:
                line = clean.count("\n", 0, idx) + 1
                print("%s: 多余的 %s 在第 %d 行" % (path, ch, line))
                ok = False
                break
            stack.pop()
    if stack and ok:
        line = clean.count("\n", 0, stack[-1][1]) + 1
        print("%s: 未闭合 %s（第 %d 行）" % (path, stack[-1][0], line))
        ok = False
    if ok:
        print("%s: 括号平衡 OK，%d 行" % (path, src.count("\n") + 1))
