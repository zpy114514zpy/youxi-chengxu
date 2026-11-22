using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 给屋子里本来就有柜门/抽屉模型、但没有交互的部件补上 E 交互。
///
/// 分类规则（用户要求）：
///   ① 薄片且竖长（高 ≥ 宽）→ 柜门，平开：左柜向左开、右柜向右开，绕各自的侧边铰链转。
///   ② 其余（抽屉盒、横向薄面板）→ 抽屉，直着朝外拉出，不往里推、不左右滑。
/// 开口方向一律用“射线测通空 + 朝玩家”判定，不靠猜哪个轴更短。
/// 运行时装配，不改场景文件。
/// </summary>
public static class DrawerSetup
{
    static readonly Vector3[] Axes =
    {
        Vector3.right, Vector3.left, Vector3.forward, Vector3.back
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Setup()
    {
        if (!GameObject.Find("BP_Door_Inside_A5")) return;
        RemoveBrokenDrawers();
        int added = AttachToPanels();
        int doors = ClassifyAll();
        Debug.Log("[抽屉] 新装交互 " + added + " 个；按“柜门平开 / 抽屉直拉”分类 " + doors + " 个。");
    }

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    static Renderer Visual(FurnitureOpenable o)
    {
        var r = o.GetComponent<Renderer>();
        return r ? r : o.GetComponentInChildren<Renderer>();
    }

    static float Clearance(Vector3 origin, Vector3 dir, Transform self)
    {
        const float max = 1.6f;
        var hits = Physics.RaycastAll(origin + dir * .05f, dir, max, ~0, QueryTriggerInteraction.Ignore);
        float best = max;
        foreach (var h in hits)
        {
            if (!h.collider) continue;
            var t = h.collider.transform;
            if (self && (t == self || t.IsChildOf(self))) continue;
            if (h.distance > .02f && h.distance < best) best = h.distance;
        }
        return best;
    }

    /// <summary>不依赖玩家的开口方向：四个水平方向里前面最空的那个。</summary>
    static Vector3 GuessOutward(Vector3 center, Transform self)
    {
        Vector3 best = Vector3.forward;
        float bestClearance = -1f;
        foreach (var dir in Axes)
        {
            float c = Clearance(center, dir, self);
            if (c > bestClearance) { bestClearance = c; best = dir; }
        }
        return best;
    }

    /// <summary>分类 + 设置 travel / openAngle / 铰链侧。</summary>
    static int ClassifyAll()
    {
        var list = new List<FurnitureOpenable>(Object.FindObjectsOfType<FurnitureOpenable>());
        var panels = new List<(FurnitureOpenable o, Bounds b)>();
        foreach (var o in list)
        {
            if (!o) continue;
            var r = Visual(o);
            if (!r) continue;
            panels.Add((o, r.bounds));
        }

        // 先把柜门按“同款 + 同高度 + 相邻”聚成组，一个柜子的两扇门必须在同一组里。
        var doorGroups = BuildDoorGroups(panels);

        int doors = 0;
        foreach (var (o, b) in panels)
        {
            Vector3 s = b.size;
            float minDim = Mathf.Min(s.x, Mathf.Min(s.y, s.z));
            float width = Mathf.Max(s.x, s.z);

            bool thin = minDim < .10f;
            bool upright = thin && s.y >= width * 1.05f;      // 竖长的薄片 = 柜门

            if (upright)
            {
                var group = doorGroups[o];
                o.slide = false;
                o.openAngle = 88f;
                o.speed = 2.2f;
                o.hingeOnRight = HingeIsRight(b, group.Item2, group.Item3, o.transform);
                doors++;
                continue;
            }

            // 整件家具（大衣柜的柜体网格）不算可开面板，拉出来会很怪
            float maxDim = Mathf.Max(s.x, Mathf.Max(s.y, s.z));
            if (minDim > .12f && maxDim > 1.4f)
            {
                o.slide = true;
                o.travel = .02f;
                o.speed = 2.6f;
                o.enabled = false;
                continue;
            }

            o.slide = true;
            o.speed = 2.6f;
            // 拉出距离 = 进深（水平较短轴）的 82%
            float depth = Mathf.Min(s.x, s.z);
            o.travel = Mathf.Clamp(depth * .82f, .18f, .36f);
        }
        return doors;
    }

    /// <summary>
    /// 同款柜门按“距离 1.3m 内 + 高度差 0.6m 内”聚成一组，
    /// 并返回每组的合并框与统一朝外的法线。同一组的法线必须一致，否则左右会判反。
    /// </summary>
    static Dictionary<FurnitureOpenable, (int id, Bounds groupBounds, Vector3 outward)> BuildDoorGroups(
        List<(FurnitureOpenable o, Bounds b)> panels)
    {
        var result = new Dictionary<FurnitureOpenable, (int, Bounds, Vector3)>();
        var doors = new List<(FurnitureOpenable o, Bounds b, string mesh)>();
        foreach (var (o, b) in panels)
        {
            Vector3 s = b.size;
            float minDim = Mathf.Min(s.x, Mathf.Min(s.y, s.z));
            if (minDim >= .10f) continue;
            if (s.y < Mathf.Max(s.x, s.z) * 1.05f) continue;
            doors.Add((o, b, MeshName(o)));
        }
        if (doors.Count == 0) return result;

        // 并查集做邻近聚类
        int n = doors.Count;
        var parent = new int[n];
        for (int i = 0; i < n; i++) parent[i] = i;
        int Find(int i) { while (parent[i] != i) { parent[i] = parent[parent[i]]; i = parent[i]; } return i; }
        void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) parent[b] = a; }

        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                // 双开门常常是两个不同 mesh（Door1 / Door2），不能按 mesh 名分，
                // 只看“高度接近 + 挨得近”，这才是同一个柜子上的并排两扇。
                if (Mathf.Abs(doors[i].b.size.y - doors[j].b.size.y) > .20f) continue;
                if (Vector3.Distance(doors[i].b.center, doors[j].b.center) > 1.0f) continue;
                Union(i, j);
            }

        var groups = new Dictionary<int, List<int>>();
        for (int i = 0; i < n; i++)
        {
            int root = Find(i);
            if (!groups.ContainsKey(root)) groups[root] = new List<int>();
            groups[root].Add(i);
        }

        foreach (var kv in groups)
        {
            Bounds gb = doors[kv.Value[0]].b;
            foreach (var idx in kv.Value) gb.Encapsulate(doors[idx].b);

            // 组框里较薄的水平轴就是这一整排门的法线（无符号）
            Vector3 axis = gb.size.x <= gb.size.z ? Vector3.right : Vector3.forward;
            // 符号：朝外那侧一定更空
            float pos = 0f, neg = 0f;
            foreach (var idx in kv.Value)
            {
                var t = doors[idx].o.transform;
                pos += Clearance(doors[idx].b.center, axis, t);
                neg += Clearance(doors[idx].b.center, -axis, t);
            }
            Vector3 outward = pos >= neg ? axis : -axis;

            foreach (var idx in kv.Value)
                result[doors[idx].o] = (kv.Key, gb, outward);
        }
        return result;
    }

    /// <summary>组内按沿 right 的位置分左右：在组中心左边的是左门（铰链在左），右边的是右门。</summary>
    static bool HingeIsRight(Bounds self, Bounds groupBounds, Vector3 outward, Transform selfTransform)
    {
        Vector3 right = Vector3.Cross(Vector3.up, outward).normalized;
        float offset = Vector3.Dot(self.center - groupBounds.center, right);
        if (Mathf.Abs(offset) < .03f)
        {
            // 组里只有这一扇：哪边更空哪边是把手侧，铰链在相反侧
            float r = Clearance(self.center, right, selfTransform);
            float l = Clearance(self.center, -right, selfTransform);
            return r <= l;
        }
        return offset > 0f;
    }

    static string MeshName(FurnitureOpenable o)
    {
        var filter = o.GetComponent<MeshFilter>();
        if (!filter) filter = o.GetComponentInChildren<MeshFilter>();
        return filter && filter.sharedMesh ? filter.sharedMesh.name : o.name;
    }

    /// <summary>清掉挂在空物体上的无效抽屉交互（书桌抽屉 1~10 那些）。</summary>
    static void RemoveBrokenDrawers()
    {
        foreach (var openable in Object.FindObjectsOfType<FurnitureOpenable>())
        {
            if (!openable) continue;
            if (openable.GetComponent<Renderer>() || openable.GetComponent<Collider>()) continue;
            if (openable.GetComponentsInChildren<Renderer>(true).Length > 0) continue;
            if (openable.GetComponentsInChildren<Collider>(true).Length > 0) continue;
            Object.Destroy(openable);
        }
    }

    static int AttachToPanels()
    {
        int added = 0;
        foreach (var renderer in Object.FindObjectsOfType<MeshRenderer>())
        {
            if (!renderer) continue;
            var filter = renderer.GetComponent<MeshFilter>();
            string meshName = filter && filter.sharedMesh ? filter.sharedMesh.name : "";
            if (!IsPanelMesh(meshName)) continue;

            var go = renderer.gameObject;
            if (go.GetComponentInParent<FurnitureOpenable>()) continue;
            if (go.GetComponentInParent<EnvelopeFragment>()) continue;

            Bounds bounds = renderer.bounds;
            float maxDim = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            if (maxDim > 2.3f || bounds.size.magnitude < .12f) continue;   // 整扇房门 / 碎片

            if (!go.GetComponent<Collider>())
            {
                var box = go.AddComponent<BoxCollider>();
                Vector3 scale = go.transform.lossyScale;
                box.center = go.transform.InverseTransformPoint(bounds.center);
                box.size = new Vector3(
                    Mathf.Max(.04f, bounds.size.x) / Mathf.Max(.0001f, Mathf.Abs(scale.x)),
                    Mathf.Max(.04f, bounds.size.y) / Mathf.Max(.0001f, Mathf.Abs(scale.y)),
                    Mathf.Max(.04f, bounds.size.z) / Mathf.Max(.0001f, Mathf.Abs(scale.z)));
            }

            var openable = go.AddComponent<FurnitureOpenable>();
            openable.slide = true;
            openable.speed = 2.6f;
            openable.travel = .28f;
            openable.slideDirection = Vector3.forward;
            added++;
        }
        return added;
    }

    /// <summary>柜门 / 抽屉面板。排除 LOD、房门、窗、玻璃。</summary>
    static bool IsPanelMesh(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name.Contains("_LOD")) return false;
        if (name.StartsWith("SM_Door_Inside") || name.StartsWith("SM_Door_Outside")) return false;
        if (name.Contains("Window") || name.Contains("Glass")) return false;

        if (name.StartsWith("SM_Drawer_A1_Cab")) return true;
        if (name.StartsWith("SM_WorkingTable_A1_Cab")) return true;
        if (name.StartsWith("SM_WorkingTable_A2_Cab")) return true;
        if (name.StartsWith("SM_Shelf_TV_A1_Door")) return true;
        if (name.StartsWith("Draw ") && name.Contains("_low")) return true;

        return name.Contains("Door") || name.Contains("Drawer") || name.Contains("_Cab")
               || name.Contains("Cabinet") || name.Contains("Wardrobe")
               || name.Contains("Closet") || name.Contains("Cupboard");
    }
}
