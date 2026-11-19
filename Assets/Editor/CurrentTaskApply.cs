using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class CurrentTaskApply
{
    [MenuItem("Tools/Apply Bed, Toy And Spawn Fixes")]
    public static void Run()
    {
        var scene=EditorSceneManager.GetActiveScene();
        if(scene.name!="Level 1")throw new InvalidOperationException("Open Level 1 before applying scene fixes.");
        var roots=scene.GetRootGameObjects();
        GameObject Find(string name)=>roots.FirstOrDefault(g=>g.name==name);
        var log=new System.Text.StringBuilder();

        var desk=Find("model");
        if(!desk)throw new InvalidOperationException("Desk model was not found.");
        foreach(var box in desk.GetComponentsInChildren<BoxCollider>(true))
        {
            var mesh=box.GetComponent<MeshFilter>();
            if(!mesh||!mesh.sharedMesh)continue;
            if(box.transform==desk.transform)
            {
                // A mesh collider follows the rendered desk exactly, including its imported scale.
                var accurate=desk.GetComponent<MeshCollider>();
                if(!accurate)accurate=Undo.AddComponent<MeshCollider>(desk);
                accurate.sharedMesh=mesh.sharedMesh;
                accurate.convex=false;
                Undo.DestroyObjectImmediate(box);
                log.AppendLine("Replaced oversized desk box with its rendered mesh collider");
            }
            else if(box.name.StartsWith("Handle ") && box.bounds.size.magnitude>1f)
            {
                // Drawer faces retain their own colliders; the handle boxes were several metres wide.
                Undo.RecordObject(box,"Disable malformed handle collider");
                box.enabled=false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(box);
                log.AppendLine("Disabled malformed collider on "+box.name);
            }
        }
        Physics.SyncTransforms();

        var player=Find("Player");
        var capsule=player?player.GetComponentInChildren<CapsuleCollider>():null;
        if(!capsule)throw new InvalidOperationException("Player capsule was not found.");
        var ground=Physics.RaycastAll(capsule.bounds.center+Vector3.up*.6f,Vector3.down,2f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore)
            .Where(hit=>!hit.collider.transform.IsChildOf(player.transform) && hit.normal.y>.75f && hit.point.y<=capsule.bounds.center.y-.2f)
            .OrderByDescending(hit=>hit.point.y).FirstOrDefault();
        if(ground.collider)
        {
            float lift=ground.point.y+.02f-capsule.bounds.min.y;
            if(lift>.005f && lift<.3f)
            {
                Undo.RecordObject(player.transform,"Lift player above floor");
                player.transform.position+=Vector3.up*lift;
                PrefabUtility.RecordPrefabInstancePropertyModifications(player.transform);
                log.AppendLine("Lifted player "+lift.ToString("F3")+" m above floor");
            }
        }

        var bed=Find("SM_Bed_A1");
        var puzzle=Find("Childhood Story Puzzle");
        if(!bed||!puzzle)throw new InvalidOperationException("Main bed or puzzle was not found.");
        var placements=puzzle.GetComponentsInChildren<StoryPlacement>(true).OrderBy(p=>p.order).ToArray();
        if(placements.Length!=4)throw new InvalidOperationException("Expected exactly four placement points.");
        float[] zPositions={-93.75f,-93.30f,-92.85f,-92.40f};
        for(int i=0;i<placements.Length;i++)
        {
            float surface=float.NegativeInfinity;
            foreach(var hit in Physics.RaycastAll(new Vector3(-22.70f,5.5f,zPositions[i]),Vector3.down,3f))
                if(hit.collider.transform.IsChildOf(bed.transform))surface=Mathf.Max(surface,hit.point.y);
            if(float.IsNegativeInfinity(surface))throw new InvalidOperationException("Main bed has no collider below point "+i);
            Undo.RecordObject(placements[i].transform,"Move toy point to main bed");
            placements[i].transform.position=new Vector3(-22.70f,surface+.025f,zPositions[i]);
            PrefabUtility.RecordPrefabInstancePropertyModifications(placements[i].transform);
            log.AppendLine("Point "+(i+1)+" "+placements[i].transform.position);
        }

        foreach(var info in new[]{
            new[]{"toy_1","story_pink","粉色独眼玩偶","她把快乐藏在独眼玩偶的微笑里。"},
            new[]{"toy4","story_cat","小猫玩偶","绘本里的小猫，等待回到床上的第一个位置。"},
            new[]{"toy_3","story_bear","小熊玩偶","暖和的小熊玩偶，绘本摆放顺序中的第三位。"},
            new[]{"猪猪厨师3d模型","story_pig","小猪厨师玩偶","喜欢做点心的小猪，绘本摆放顺序中的第二位。"}})
        {
            var toy=Find(info[0]);if(!toy)throw new InvalidOperationException("Toy missing: "+info[0]);
            var pickup=toy.GetComponent<PickupItem>();
            if(!pickup)pickup=Undo.AddComponent<PickupItem>(toy);
            Undo.RecordObject(pickup,"Set toy inventory description");
            pickup.storyId=info[1];pickup.displayName=info[2];pickup.itemType="玩偶";pickup.description=info[3];
            PrefabUtility.RecordPrefabInstancePropertyModifications(pickup);
            if(!toy.GetComponentInChildren<Collider>())
            {
                var r=toy.GetComponentInChildren<Renderer>();
                if(r)
                {
                    var box=Undo.AddComponent<BoxCollider>(toy);
                    box.center=toy.transform.InverseTransformPoint(r.bounds.center);
                    var s=toy.transform.lossyScale;
                    box.size=new Vector3(Mathf.Max(.1f,r.bounds.size.x/Mathf.Abs(s.x)),Mathf.Max(.1f,r.bounds.size.y/Mathf.Abs(s.y)),Mathf.Max(.1f,r.bounds.size.z/Mathf.Abs(s.z)));
                }
            }
            if(!toy.GetComponent<ProximityOutline>())Undo.AddComponent<ProximityOutline>(toy);
            log.AppendLine("Outlined and configured "+info[0]);
        }
        Physics.SyncTransforms();
        EditorSceneManager.MarkSceneDirty(scene);
        if(!EditorSceneManager.SaveScene(scene))throw new IOException("Scene save failed.");
        var report=Path.Combine(Application.dataPath,"../Temp/CurrentTaskApply.txt");
        File.WriteAllText(report,log.ToString());
        Debug.Log("Applied current task scene fixes: "+report);
    }
}
