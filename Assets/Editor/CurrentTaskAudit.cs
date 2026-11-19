using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CurrentTaskAudit
{
    [InitializeOnLoadMethod]
    static void AfterReload()
    {
        EditorApplication.delayCall += () => { if(EditorSceneManager.GetActiveScene().name=="Level 1")Run(); };
    }
    [MenuItem("Tools/Current Task Audit")]
    public static void Run()
    {
        var scene=EditorSceneManager.GetActiveScene();
        var roots=scene.GetRootGameObjects();
        GameObject Find(string name)=>roots.FirstOrDefault(g=>g.name==name);
        var player=Find("Player");
        var capsule=player.GetComponentInChildren<CapsuleCollider>();
        Physics.SyncTransforms();
        var file=Path.Combine(Application.dataPath,"../Temp/CurrentTaskAudit.txt");
        using(var w=new StreamWriter(file))
        {
            w.WriteLine("Player="+player.transform.position+" scale="+player.transform.lossyScale);
            w.WriteLine("Capsule="+capsule.bounds.center+" size="+capsule.bounds.size+" local="+capsule.center+"/"+capsule.height+"/"+capsule.radius);
            var cam=Camera.main;if(cam)w.WriteLine("Camera="+cam.transform.position+" parent="+(cam.transform.parent?cam.transform.parent.name:"none"));
            float radius=capsule.radius*Mathf.Max(Mathf.Abs(capsule.transform.lossyScale.x),Mathf.Abs(capsule.transform.lossyScale.z));
            float half=Mathf.Max(0,capsule.height*.5f-capsule.radius)*Mathf.Abs(capsule.transform.lossyScale.y);
            var center=capsule.bounds.center;
            foreach(var c in Physics.OverlapCapsule(center+Vector3.down*half,center+Vector3.up*half,radius,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            {
                if(c.transform.IsChildOf(player.transform))continue;
                bool penetrates=Physics.ComputePenetration(capsule,capsule.transform.position,capsule.transform.rotation,c,c.transform.position,c.transform.rotation,out var direction,out float depth);
                w.WriteLine("OVERLAP "+c.name+" parent="+(c.transform.root?c.transform.root.name:"-")+" center="+c.bounds.center+" size="+c.bounds.size+" penetration="+penetrates+" depth="+depth+" direction="+direction);
            }
            foreach(var g in roots.Where(g=>g.name.Contains("toy")||g.name.Contains("Toy")||g.name.Contains("猪")||g.name=="SM_Bed_A1"||g.name=="model"))
            {var r=g.GetComponentInChildren<Renderer>();w.WriteLine("OBJECT "+g.name+" pos="+g.transform.position+" bounds="+(r?r.bounds.center+"/"+r.bounds.size:"-")+" pickup="+(g.GetComponent<PickupItem>()?g.GetComponent<PickupItem>().storyId:"-") );}
            var puzzle=Find("Childhood Story Puzzle");
            if(puzzle)foreach(var p in puzzle.GetComponentsInChildren<StoryPlacement>(true))w.WriteLine("PLACEMENT "+p.name+" "+p.transform.position+" id="+p.requiredItemId);
            var bed=Find("SM_Bed_A1");
            if(bed)
            {
                foreach(var z in new[]{-93.80f,-93.35f,-92.90f,-92.45f})
                {
                    var hits=Physics.RaycastAll(new Vector3(-22.70f,5.5f,z),Vector3.down,3f);
                    foreach(var h in hits.Where(h=>h.collider.transform.IsChildOf(bed.transform)))w.WriteLine("BED_RAY z="+z+" y="+h.point.y+" hit="+h.collider.name);
                }
                foreach(var c in bed.GetComponentsInChildren<Collider>())w.WriteLine("BED_COLL "+c.name+" "+c.bounds.center+"/"+c.bounds.size);
            }
            var model=Find("model");
            if(model)foreach(var c in model.GetComponentsInChildren<Collider>(true))
                w.WriteLine("MODEL_COLL "+c.name+" type="+c.GetType().Name+" enabled="+c.enabled+" parent="+(c.transform.parent?c.transform.parent.name:"-")+" center="+c.bounds.center+" size="+c.bounds.size+" mesh="+(c is MeshCollider mc && mc.sharedMesh?mc.sharedMesh.name:"-"));
            if(model)foreach(var r in model.GetComponentsInChildren<Renderer>(true))
                w.WriteLine("MODEL_RENDER "+r.name+" center="+r.bounds.center+" size="+r.bounds.size);
            foreach(var g in roots.Where(g=>g.name.Contains("Player")||g.name.Contains("Spawn")))w.WriteLine("SPAWN_ROOT "+g.name+" "+g.transform.position);
            foreach(var pickup in Object.FindObjectsOfType<PickupItem>(true))w.WriteLine("PICKUP "+pickup.name+" id="+pickup.storyId+" root="+pickup.transform.root.name+" active="+pickup.gameObject.activeSelf);
        }
        Debug.Log("Current task audit written: "+file);
    }
}
