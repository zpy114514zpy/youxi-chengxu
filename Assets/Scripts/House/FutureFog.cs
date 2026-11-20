using UnityEngine;

// Keep the far world uncertain while nearby interior rooms stay readable.
public sealed class FutureFog : MonoBehaviour
{
    void Start()
    {
        RenderSettings.fog=true;
        RenderSettings.fogMode=FogMode.Linear;
        RenderSettings.fogColor=new Color(.93f,.95f,.96f);
        RenderSettings.fogStartDistance=8f;
        RenderSettings.fogEndDistance=24f;
        var camera=Camera.main;
        if(camera){camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=RenderSettings.fogColor;}
        Seal("BP_Door_Outside_A3");
        Seal("BP_Door_Outside_B");
    }

    static void Seal(string name)
    {
        var door=GameObject.Find(name);
        if(!door)return;
        if(!door.GetComponent<ExitBoundary>())door.AddComponent<ExitBoundary>();
        var renderers=door.GetComponentsInChildren<Renderer>(true);
        if(renderers.Length==0)return;
        Bounds bounds=renderers[0].bounds;
        for(int i=1;i<renderers.Length;i++)bounds.Encapsulate(renderers[i].bounds);
        var barrier=new GameObject("时光边界 - "+name);
        barrier.transform.SetPositionAndRotation(bounds.center,Quaternion.identity);
        var box=barrier.AddComponent<BoxCollider>();
        box.size=new Vector3(Mathf.Max(bounds.size.x,.28f),Mathf.Max(bounds.size.y,2.1f),Mathf.Max(bounds.size.z,.28f));
    }
}
