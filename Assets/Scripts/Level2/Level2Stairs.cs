using UnityEngine;

/// <summary>
/// 一楼上二楼那道楼梯：补一块看不见的斜面碰撞。
///
/// 问题出在这段楼梯是从 Unreal 整块导出的，整跑只有一个 MeshCollider。
/// 玩家控制器爬台阶时判断的是 "collider.bounds.max.y - 脚底"，只对 ≤32cm 的矮物抬一次；
/// 而整段楼梯的包围盒有两米多高，于是每一级都被判成“太高，爬不上去”——
/// 从二楼下来没问题（往下走一级落一级），想再上去就永远卡在第一级。
///
/// 这里沿楼梯的实际坡度铺一块斜板，玩家踩在斜板上走就顺了。
/// 斜板只在运行时生成，场景文件不动；原来的整段碰撞会被关掉，免得台阶立面从斜板下面顶出来。
/// </summary>
public sealed class Level2Stairs : MonoBehaviour
{
    // 采样点离楼梯两端的距离比例：太靠边容易采到平台或楼板。
    const float Edge = .10f;

    public void Setup()
    {
        var stair = FindStair();
        if (!stair)
        {
            Debug.LogWarning("[第二关] 找不到一楼上二楼的楼梯，先不补斜面");
            return;
        }

        var b = L2.BoundsOf(stair);
        if (b.size.y < .4f)
        {
            Debug.LogWarning($"[第二关] 楼梯包围盒异常 {L2.Fmt(b.size)}，跳过斜面");
            return;
        }
        Physics.SyncTransforms();       // 量包围盒之前先把刚加载的场景变换同步一遍，射线才打得准

        bool alongZ = b.size.z >= b.size.x;          // 水平面上更长的那一轴就是上楼方向
        Vector3 c = b.center;
        Vector3 p0, p1;
        if (alongZ)
        {
            float z0 = Mathf.Lerp(b.min.z, b.max.z, Edge);
            float z1 = Mathf.Lerp(b.min.z, b.max.z, 1f - Edge);
            p0 = new Vector3(c.x, 0f, z0);
            p1 = new Vector3(c.x, 0f, z1);
        }
        else
        {
            float x0 = Mathf.Lerp(b.min.x, b.max.x, Edge);
            float x1 = Mathf.Lerp(b.min.x, b.max.x, 1f - Edge);
            p0 = new Vector3(x0, 0f, c.z);
            p1 = new Vector3(x1, 0f, c.z);
        }

        if (!SampleGround(p0, b, stair, out float y0) || !SampleGround(p1, b, stair, out float y1))
        {
            Debug.LogWarning("[第二关] 楼梯两端射线都没打到踏面，先不补斜面（可能在别的位置）");
            return;
        }

        // 采到的两个高度差太小，说明两头都落在平台/楼板上，再往里挪一点重采一次。
        if (Mathf.Abs(y1 - y0) < .5f)
        {
            if (alongZ)
            {
                p0.z = Mathf.Lerp(b.min.z, b.max.z, .04f);
                p1.z = Mathf.Lerp(b.min.z, b.max.z, .96f);
            }
            else
            {
                p0.x = Mathf.Lerp(b.min.x, b.max.x, .04f);
                p1.x = Mathf.Lerp(b.min.x, b.max.x, .96f);
            }
            if (SampleGround(p0, b, stair, out float ry0) && SampleGround(p1, b, stair, out float ry1))
            {
                y0 = ry0; y1 = ry1;
            }
        }

        if (Mathf.Abs(y1 - y0) < .3f)
        {
            Debug.LogWarning($"[第二关] 楼梯两端高度几乎一样（{y0:F2} / {y1:F2}），不像斜跑楼梯，先不补斜面");
            return;
        }

        // A 低 B 高，斜板从底铺到顶。
        Vector3 a = new Vector3(p0.x, y0 + .03f, p0.z);
        Vector3 d = new Vector3(p1.x, y1 + .03f, p1.z);
        Vector3 low = a.y <= d.y ? a : d;
        Vector3 high = a.y <= d.y ? d : a;

        Vector3 dir = high - low;
        float slopeLength = dir.magnitude;
        float width = (alongZ ? b.size.x : b.size.z) * .86f;
        float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        float pitch = -Mathf.Atan2(dir.y, new Vector2(dir.x, dir.z).magnitude) * Mathf.Rad2Deg;

        var rot = Quaternion.Euler(pitch, yaw, 0f);
        const float thickness = .18f;
        // 盒子的“上表面”正好压在两个采样点上：中心沿法线往下推半个厚度。
        // 不这么做的话，盒子有一半埋在台阶里，玩家踩上去会陷一个厚度。
        Vector3 center = (low + high) * .5f - (rot * Vector3.up) * (thickness * .5f);

        var go = new GameObject("楼梯·斜面碰撞");
        go.transform.SetPositionAndRotation(center, rot);
        go.layer = stair.gameObject.layer;
        var box = go.AddComponent<BoxCollider>();
        box.size = new Vector3(width, thickness, slopeLength);

        // 原来的整段碰撞关掉：它自己不参与爬台阶（包围盒太高），留着只会让台阶立面顶到玩家腿上。
        int off = 0;
        foreach (var col in stair.GetComponentsInChildren<Collider>(true))
        {
            if (!col || col.isTrigger) continue;
            col.enabled = false;
            off++;
        }

        go.SetActive(true);
        Debug.Log($"[第二关] 楼梯斜面已补：宽 {width:F2}m、斜长 {slopeLength:F2}m、坡度 {Mathf.Abs(pitch):F0}° ，" +
                  $"底 {L2.Fmt(low)} → 顶 {L2.Fmt(high)}，关掉原碰撞 {off} 个（楼梯 {L2.Fmt(b.size)}）");
    }

    /// <summary>取包围盒里带 Renderer 最多的那个：LOD 子物体、栏杆都是零散小块，本体才是最大的。</summary>
    static Transform FindStair()
    {
        Transform best = null;
        int bestCount = -1;
        foreach (var t in FindObjectsOfType<Transform>(true))
        {
            if (t.name.IndexOf("BP_Stairs_D_1-2-Attic", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (!t.gameObject.scene.IsValid()) continue;
            int n = t.GetComponentsInChildren<Renderer>(true).Length;
            if (n > bestCount) { bestCount = n; best = t; }
        }
        return best;
    }

    /// <summary>
    /// 只认楼梯自己的踏面：从楼梯顶上往下打，取“属于这段楼梯”的碰撞体里最低的那个命中点。
    /// 不这么筛的话，第一个打到的会是二楼的楼板（楼梯正上方就是楼板），底部就采不到真实高度。
    /// </summary>
    static bool SampleGround(Vector3 at, Bounds b, Transform stair, out float y)
    {
        y = 0f;
        Vector3 from = new Vector3(at.x, b.max.y + 1.2f, at.z);
        float distance = b.size.y + 2.6f;
        float lowest = float.MaxValue;
        foreach (var hit in Physics.RaycastAll(from, Vector3.down, distance,
                     Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (!hit.collider || hit.collider.isTrigger) continue;
            var t = hit.collider.transform;
            if (t != stair && !t.IsChildOf(stair)) continue;
            if (hit.point.y < lowest) lowest = hit.point.y;
        }
        if (lowest == float.MaxValue) return false;
        y = lowest;
        return true;
    }
}
