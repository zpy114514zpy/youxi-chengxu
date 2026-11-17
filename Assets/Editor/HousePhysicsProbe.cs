using UnityEditor;
using UnityEngine;
using System.Linq;
using Object = UnityEngine.Object;

[InitializeOnLoad]
static class HousePhysicsProbe
{
    static HousePhysicsProbe() { EditorApplication.delayCall += Check; }
    static void Check()
    {
        var p=Object.FindObjectOfType<PlayerController>();
        if(!p)return;
        Physics.SyncTransforms();
        var cap=p.GetComponentInChildren<CapsuleCollider>();
        Debug.Log("HOUSEPROBE player "+p.transform.position.ToString("F4")+" capsuleBottom "+cap.bounds.min.y);
        foreach(var s in p.GetComponentsInChildren<SkinnedMeshRenderer>()){
            var m=new Mesh();s.BakeMesh(m);var min=m.vertices.Min(v=>s.transform.TransformPoint(v).y);
            Debug.Log("HOUSEPROBE skin "+s.name+" min "+min+" scale "+s.transform.lossyScale+" vertices "+m.vertexCount);
            Object.DestroyImmediate(m);
        }
        foreach(var b in p.GetComponentsInChildren<Transform>().Where(x=>x.name.Contains("Foot")||x.name.Contains("Toe")||x.name.EndsWith("Hips")))
            Debug.Log("HOUSEPROBE bone "+b.name+" "+b.position.ToString("F4"));
        foreach(var h in Physics.RaycastAll(cap.bounds.center,Vector3.down,10).OrderBy(x=>x.distance).Take(6))
            Debug.Log("HOUSEPROBE surface "+h.collider.transform.parent?.name+"/"+h.collider.name+" y "+h.point.y);
        foreach(var group in Object.FindObjectsOfType<MeshFilter>()
            .Where(x=>x.GetComponent<MeshCollider>() && (x.name.StartsWith("SM_Floor_") || x.name.StartsWith("SM_Stairs_D") || x.name.StartsWith("SM_Terrace_Stairs")))
            .GroupBy(x=>x.name)){
            int expected=0, hit=0;float min=999,max=-999;
            foreach(var f in group){
                var col=f.GetComponent<MeshCollider>();
                using(var data=UnityEditor.MeshUtility.AcquireReadOnlyMeshData(f.sharedMesh)){
                    var m=data[0];
                    var v=new Unity.Collections.NativeArray<Vector3>(m.vertexCount,Unity.Collections.Allocator.Temp);
                    m.GetVertices(v);
                    for(int sub=0;sub<m.subMeshCount;sub++){
                        var sm=m.GetSubMesh(sub);
                        var idx=new Unity.Collections.NativeArray<int>(sm.indexCount,Unity.Collections.Allocator.Temp);
                        m.GetIndices(idx,sub);
                        for(int i=0;i<idx.Length;i+=3){
                            Vector3 a=f.transform.TransformPoint(v[idx[i]]),b=f.transform.TransformPoint(v[idx[i+1]]),c=f.transform.TransformPoint(v[idx[i+2]]);
                            Vector3 n=Vector3.Cross(b-a,c-a);
                            if(n.sqrMagnitude<0.00000001f || n.normalized.y<0.75f)continue;
                            var mid=(a+b+c)/3;
                            min=Mathf.Min(min,mid.y);max=Mathf.Max(max,mid.y);expected++;
                            if(col.Raycast(new Ray(mid+Vector3.up*0.025f,Vector3.down),out var h,0.05f))hit++;
                        }
                        idx.Dispose();
                    }
                    v.Dispose();
                }
            }
            Debug.Log($"HOUSECOVER {group.Key} instances={group.Count()} support={hit}/{expected} y={min:F2}..{max:F2}");
        }
        foreach(var b in new[]{false,true}){
            var orig=Physics.queriesHitBackfaces; Physics.queriesHitBackfaces=b;
            var rb=Physics.RaycastAll(cap.bounds.center+Vector3.up*4,Vector3.down,12).OrderBy(h=>h.distance);
            Debug.Log("HOUSELEVEL backfaces "+b+" "+string.Join(" / ",rb.Select(h=>h.collider.name+"@"+h.point.y.ToString("F3"))));
            Physics.queriesHitBackfaces=orig;
        }
        foreach(var g in Object.FindObjectsOfType<MeshFilter>().Where(x=>x.name.StartsWith("SM_Floor")).GroupBy(x=>x.sharedMesh))
        {
            var x=g.First();var c=x.transform.parent.GetComponentsInChildren<Collider>();
            Debug.Log("HOUSEPROBE floor "+x.name+" instances "+g.Count()+" colliders "+string.Join(",",c.Select(v=>v is MeshCollider mc ? mc.sharedMesh?mc.sharedMesh.name:"NULL":v.GetType().Name))+" bounds "+x.sharedMesh.bounds);
        }
    }
}


