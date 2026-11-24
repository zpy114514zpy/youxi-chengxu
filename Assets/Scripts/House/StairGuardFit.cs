using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 楼梯口“走不过去”的真正原因：坡道两侧那两块隐形护栏（Left Guard / Right Guard）。
///
/// 它们是跟着坡道一起倾斜的薄板（本地尺寸 0.18 × 1.30 × 5.15，随坡道转了约 -29.7°），
/// 本来只该护住“楼梯台阶”那一段。但它们的长度比坡道本身长，**下摆一直伸到一楼地面层**
/// （世界 z 到 -106.50，而坡道低端只到 -105.95），相当于在楼梯口立了一堵看不见的墙。
/// 玩家从二楼下来往客厅方向走，就被这堵墙挡在 x≈-20.7 的位置 —— 就是“刚下楼梯走不过去”。
///
/// 这里把它们靠地面那一端裁掉，让护栏只覆盖真正的台阶段，楼梯口就通了。
/// 只改 BoxCollider 的长度，不动场景文件、不动渲染。
/// </summary>
[DisallowMultipleComponent]
public sealed class StairGuardFit : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (SceneManager.GetActiveScene().name != "Level 1") return;
        if (!GameObject.Find("BP_Door_Inside_A5")) return;     // 只在这栋二层住宅里动手
        Fit();
    }

    static void Fit()
    {
        Collider ramp = null;
        var guards = new System.Collections.Generic.List<BoxCollider>();
        foreach (var c in Object.FindObjectsOfType<Collider>())
        {
            if (!c || c.isTrigger) continue;
            if (c.name == "Stair Walk Ramp") { if (!ramp) ramp = c; continue; }
            var box = c as BoxCollider;
            if (box && (box.name == "Left Guard" || box.name == "Right Guard")) guards.Add(box);
        }
        if (!ramp || guards.Count == 0)
        {
            Debug.LogWarning("[楼梯护栏] 没找到坡道或护栏碰撞体，跳过裁剪。");
            return;
        }

        // 南端退到坡道起点往北 0.5m：楼梯最后半米（离地只有 20~30cm）不再有护栏，
        // 玩家从楼梯下来可以直接侧身走进客厅，不用绕到栏杆尽头。
        float wantMinZ = ramp.bounds.min.z + .50f;
        int trimmed = 0;
        foreach (var box in guards)
            if (TrimLowerEnd(box, wantMinZ)) trimmed++;

        Debug.Log("[楼梯护栏] 坡道低端 z=" + ramp.bounds.min.z.ToString("F2")
            + "，护栏下摆原来伸到了地面层；本次裁短 " + trimmed + " 块（目标 z≥"
            + wantMinZ.ToString("F2") + "），楼梯口现在能正常走出来。");
    }

    /// <summary>把盒子靠“世界 z 更小”的那一端裁短到 wantMinZ。</summary>
    static bool TrimLowerEnd(BoxCollider box, float wantMinZ)
    {
        Bounds before = box.bounds;
        if (before.min.z >= wantMinZ - .005f) return false;

        Transform t = box.transform;
        float half = box.size.z * .5f;
        // 盒子的两端在世界里的 z，哪一端更小就是“靠楼梯口”的那一端
        bool minusEndIsLower =
            t.TransformPoint(box.center + new Vector3(0f, 0f, -half)).z
            <= t.TransformPoint(box.center + new Vector3(0f, 0f, half)).z;

        // 本地 z 方向每 1 单位在世界 z 上的投影（薄板是斜的，不能按 1:1 算）
        float perUnit = Mathf.Abs(t.TransformVector(Vector3.forward).z);
        if (perUnit < .2f) perUnit = 1f;

        float cut = Mathf.Clamp((wantMinZ - before.min.z) / perUnit, 0f, box.size.z * .85f);
        if (cut < .01f) return false;

        Vector3 size = box.size;
        size.z -= cut;
        box.size = size;

        Vector3 center = box.center;
        center.z += minusEndIsLower ? cut * .5f : -cut * .5f;
        box.center = center;

        Physics.SyncTransforms();
        Debug.Log("[楼梯护栏] " + box.name + " 南端从 z=" + before.min.z.ToString("F2")
            + " 裁到 z=" + box.bounds.min.z.ToString("F2")
            + "（缩短 " + cut.ToString("F2") + "m）", box);
        return true;
    }
}
