using UnityEngine;

/// <summary>
/// 一楼可走的地面 + “掉到地板下面”的兜底。
///
/// 这套房屋模型的地板是一块块 1.4m 的“地砖”预制体（SM_Floor_1-2-Attic_*），
/// 但一楼只在 z≈-100.2、-96.0、-91.8 这几排铺了砖，客厅 / 餐厅那一整片
/// （x≈-24.6~-16.4，z≈-100.5~-92.2）是空的：沙发、茶几、地毯全悬在洞口上面。
/// 玩家从二楼楼梯下来、往客厅一走就直接漏到室外地面（y≈-0.2），再也上不来 ——
/// 表现就是“一楼客厅去不了”。
///
/// 这里在真实地面下方 1cm 补一层看不见的碰撞板：
///   · 只补一楼室内，范围按房间轮廓裁；
///   · 楼梯口（x -23.15~-20.65，z -106.5~-101.0）整块留空，不会给下楼的路垫出台阶；
///   · 全部运行时生成，不改场景、不动任何渲染。
/// </summary>
[DisallowMultipleComponent]
public sealed class FirstFloorAccess : MonoBehaviour
{
    const float DefaultFloorY = 0.60f;      // 一楼地面实测 0.600

    float floorY = DefaultFloorY;
    float nextRescue;
    bool reachedLogged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        // 只在这栋二层住宅里装配（有一号门），别的关卡不掺和。
        if (!GameObject.Find("BP_Door_Inside_A5")) return;
        if (FindObjectOfType<FirstFloorAccess>()) return;
        var host = new GameObject("一楼地板安全层");
        host.AddComponent<FirstFloorAccess>().Build();
    }

    void Build()
    {
        floorY = MeasureFloor();

        // 楼梯坡道实测：x -22.98~-20.83，z -106.26~-100.94，这段必须留空。
        AddSlab("一楼地板-北侧", -24.60f, -101.00f, 8.20f, 10.50f);       // 客厅 / 餐厅 / 厨房
        AddSlab("一楼地板-楼梯西侧", -24.60f, -107.30f, 1.45f, 6.30f);     // 楼梯西边那条
        AddSlab("一楼地板-楼梯东侧", -20.65f, -107.30f, 4.25f, 6.30f);     // 楼梯东边到东墙
        AddSlab("一楼地板-楼梯南侧", -23.15f, -107.30f, 2.50f, 0.80f);     // 楼梯背后那一小条

        Debug.Log("[一楼地板] 已铺在 y=" + floorY.ToString("F3") + "（楼梯口留空）", this);
    }

    /// <summary>量出一楼真实地面高度（找不到就退回 0.60）。</summary>
    static float MeasureFloor()
    {
        foreach (var hit in Physics.RaycastAll(new Vector3(-22.4f, 1.05f, -97.4f), Vector3.down, 1.4f,
                     Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (hit.point.y > .45f && hit.point.y < .75f) return hit.point.y;
        return DefaultFloorY;
    }

    void AddSlab(string label, float x, float z, float width, float depth)
    {
        var slab = new GameObject(label);
        slab.transform.SetParent(transform, false);
        // 顶面比真实地面低 1cm：不会在地砖上顶出一条缝，也不会和模型抢渲染。
        slab.transform.position = new Vector3(x + width * .5f, floorY - .06f, z + depth * .5f);
        var box = slab.AddComponent<BoxCollider>();
        box.size = new Vector3(width, .10f, depth);
    }

    /// <summary>
    /// 兜底：不管从哪个缺口掉到一楼地板下面，都把人抬回原地的地面上，
    /// 免得玩家卡在屋外地面（y≈-0.2）出不来。
    /// 判定用胶囊包围盒的底边（脚底），不用角色根节点：根节点比脚底高约 0.74m，
    /// 直接看根节点会把“掉到屋外地面”误判成还站在地板上。
    /// </summary>
    void Update()
    {
        if (Time.time < nextRescue) return;
        nextRescue = Time.time + .5f;

        var player = FindObjectOfType<PlayerController>();
        if (!player) return;
        Vector3 p = player.transform.position;

        var capsule = player.GetComponentInChildren<CapsuleCollider>();
        float feetY = capsule ? capsule.bounds.min.y : p.y;

        // 第一次真正站到一楼时打一行日志：排查“到底下没下去”时一眼就能看出来。
        if (!reachedLogged && feetY < 1f && p.x > -24.8f && p.x < -16.2f && p.z > -107.4f && p.z < -90.3f)
        {
            reachedLogged = true;
            Debug.Log("[一楼地板] 玩家已到一楼：脚底 y=" + feetY.ToString("F2") + "，位置 " + p.ToString("F2"), player);
        }

        if (feetY > floorY - .25f) return;                       // 还在地板上，正常
        if (p.x < -24.8f || p.x > -16.2f || p.z < -107.4f || p.z > -90.3f) return;  // 屋外不救

        var target = new Vector3(p.x, p.y + (floorY + .16f - feetY), p.z);
        var body = player.GetComponent<Rigidbody>();
        if (body)
        {
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = target;
        }
        player.transform.position = target;
        Physics.SyncTransforms();
        Debug.Log("[一楼地板] 玩家掉到地板下（脚底 y=" + feetY.ToString("F2")
            + "），已抬回 " + target.ToString("F2"), player);
    }
}
