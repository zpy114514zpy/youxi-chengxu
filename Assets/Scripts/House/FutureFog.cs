using UnityEngine;
using UnityEngine.SceneManagement;

// Keep the far world uncertain while nearby interior rooms stay readable.
public sealed class FutureFog : MonoBehaviour
{
    // 一楼（客厅 / 餐厅）不要这层雾，玩家 y 低于这个高度就算在一楼
    const float FirstFloorY = 2.4f;

    bool fogOff;
    Transform player;

    void Start()
    {
        // 这一套「窗外白雾 + 纯色天光」是第一关的美术语言。
        // 第二关现在有自己的黄昏天空盒：这里如果照旧执行，会把主相机改成 SolidColor、
        // 还会把雾打开，天空盒一点都看不见 —— 所以第二关直接跳过整个 Start。
        if(SceneManager.GetActiveScene().name!="Level 1")return;

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

    // 这层雾是**以相机为圆心**的距离雾（8m 起、24m 全白）：二楼房间都小，看不出来；
    // 可一楼的客厅、餐厅离楼梯口有 10~20m，整片落在雾带里，看着就是面前糊了一堵白墙，
    // 而且人往前走它就往后退（雾跟着相机走）。所以玩家下到一楼就把雾关掉，回到二楼再恢复。
    void Update()
    {
        if(SceneManager.GetActiveScene().name!="Level 1")return;   // 第二关有自己的雾参数，不掺和
        if(!player)player=transform;                               // FutureFog 挂在玩家身上
        bool onFirstFloor=player.position.y<FirstFloorY;
        if(onFirstFloor==fogOff)return;
        fogOff=onFirstFloor;
        RenderSettings.fog=!onFirstFloor;
        Debug.Log(onFirstFloor?"[白雾] 玩家在一楼，关掉远景白雾。":"[白雾] 玩家回到二楼，恢复远景白雾。");
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
