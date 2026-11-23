using UnityEngine;

/// <summary>
/// 二楼地面的“安全层”：在二楼房间里铺一层看不见的碰撞板。
/// 房屋模型里有些地板/木条是纯装饰网格（没有碰撞体），玩家走上去会直接掉到一楼；
/// 这里在真实地面高度补一层薄板，只补二楼、且把楼梯口留空，不影响上下楼。
/// 全部在运行时生成，不改场景文件。
/// </summary>
[DisallowMultipleComponent]
public sealed class FloorSafety : MonoBehaviour
{
    const float DefaultFloorY = 3.20f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        // 只在这栋二层住宅（有一号门）里装配，其它关卡不掺和。
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Level 1") return;
        if (!GameObject.Find("BP_Door_Inside_A5")) return;
        if (FindObjectOfType<FloorSafety>()) return;
        var host = new GameObject("二楼地板安全层");
        host.AddComponent<FloorSafety>().Build();
    }

    void Build()
    {
        float floorY = MeasureFloor();
        // 二楼平面（x 约 -25 ~ -16.3，z 约 -106.7 ~ -89.3）。
        // 楼梯口（x -23.3 ~ -20.3，z -106.7 ~ -101.3）留空，否则会把下楼的路堵死。
        AddSlab("二楼地板-左侧", -24.95f, -106.70f, 1.65f, 17.40f, floorY);
        AddSlab("二楼地板-楼梯北侧", -23.30f, -101.30f, 3.00f, 12.00f, floorY);
        AddSlab("二楼地板-右侧", -20.30f, -106.70f, 4.00f, 17.40f, floorY);

        Debug.Log("[地板安全层] 已铺在 y=" + floorY.ToString("F3") + "（楼梯口留空）", this);
    }

    /// <summary>量出二楼真实地面高度（找不到就退回默认值）。</summary>
    static float MeasureFloor()
    {
        foreach (var hit in Physics.RaycastAll(new Vector3(-19f, 3.60f, -97f), Vector3.down, 1.0f,
                     Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (hit.point.y > 3.05f && hit.point.y < 3.35f) return hit.point.y;
        return DefaultFloorY;
    }

    void AddSlab(string label, float x, float z, float width, float depth, float floorY)
    {
        var slab = new GameObject(label);
        slab.transform.SetParent(transform, false);
        // 顶面比真实地面低 1cm：不会在地板上顶出一个台阶，也不会和模型抢渲染。
        slab.transform.position = new Vector3(x + width * .5f, floorY - .06f, z + depth * .5f);
        var box = slab.AddComponent<BoxCollider>();
        box.size = new Vector3(width, .10f, depth);
    }
}
