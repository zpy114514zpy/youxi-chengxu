import re
path="Assets/Level 1.unity"
text=open(path,encoding="utf-8",errors="ignore").read()
parts=re.split(r"^--- !u!(\d+) &(\d+)",text,flags=re.M)
docs=[(parts[i],parts[i+1],parts[i+2]) for i in range(1,len(parts)-1,3)]
def vec(b,k):
    m=re.search(k+r":\s*\{x:/s*([-/d.eE]+),/s*y:/s*([-/d.eE]+),/s*z:/s*([-/d.eE]+)",b)
    return tuple(float(m.group(i)) for i in (1,2,3)) if m else None
for cls,did,body in docs:
    if cls!="1001": continue
    name=None
    for m in re.finditer(r"propertyPath:/s*(/S+)/s*/n/s*(?:value:/s*(.*))?",body):
        if m.group(1)=="m_Name": name=(m.group(2) or "").strip()
    if not name: continue
    if not any(k in name for k in ("BP_Door_Inside","BP_Shelf_TV","SM_Shelf_TV","BP_BedTable","key")): continue
    pos={}
    for m in re.finditer(r"propertyPath:/s*m_LocalPosition/.(/w)/s*/n/s*value:/s*([-/d.eE]+)",body):
        pos[m.group(1)]=float(m.group(2))
    par=re.search(r"m_TransformParent:/s*/{fileID:/s*(/d+)",body)
    print(name,"pos=",(pos.get("x"),pos.get("y"),pos.get("z")),"parent=",par.group(1) if par else None)
