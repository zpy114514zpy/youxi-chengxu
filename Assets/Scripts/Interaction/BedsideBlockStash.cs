using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 把卧室里“床右边地上那块积木”收进旁边床头柜的抽屉里（运行时重挂父级，不改场景文件）。
///
/// 为什么不是直接把它 SetActive(false) 藏起来：
///   · 直着塞进抽屉，积木往往比抽屉内腔高，关着抽屉时会从柜板里穿出来，很难看；
///   · 藏起来的积木应该也捡不到 —— 玩家得先拉开抽屉。
/// 所以这里只关掉它的渲染器和碰撞体：抽屉关着 = 看不见也捡不到；
/// 玩家拉开抽屉（OpenAmount 够了）再一起打开，积木跟着抽屉滑出来，可以正常按 E 拾取。
/// </summary>
public static class BedsideBlockStash
{
    const string BedName = "SM_Bed_A1";
    const string NightstandMeshPrefix = "SM_BedTable_A1_Cab";
    /// <summary>积木离床头柜的水平距离超过这个值就不算“床边那块”。</summary>
    const float NightstandReach = 2f;
    /// <summary>同一只床头柜的两层抽屉之间只差十几厘米，用来分组。</summary>
    const float GroupDistance = .9f;

    /// <summary>把“离床头柜最近、又没被床压住的那块积木”放进抽屉。成功返回 true。</summary>
    public static bool Stash(List<Transform> blocks)
    {
        if (blocks == null || blocks.Count == 0) return false;

        var drawers = CollectNightstandDrawers();
        if (drawers.Count == 0)
        {
            Debug.LogWarning("[积木谜题] 没找到床头柜抽屉（网格名 " + NightstandMeshPrefix + "），床边那块积木保持原样。");
            return false;
        }

        Bounds bedBounds = new Bounds(Vector3.zero, Vector3.zero);

        GameObject bestBlock = null;
        FurnitureOpenable bestDrawer = null;
        float bestScore = float.MaxValue;

        foreach (var group in GroupByNightstand(drawers))
        {
            Bounds groupBounds = BoundsOf(group[0].transform);
            for (int i = 1; i < group.Count; i++) groupBounds.Encapsulate(BoundsOf(group[i].transform));
            bedBounds = NearestBedBounds(groupBounds.center);

            var drawer = TopDrawer(group);
            if (!drawer) continue;

            foreach (var piece in blocks)
            {
                if (!piece) continue;
                Bounds bounds = BoundsOf(piece);
                if (Mathf.Abs(bounds.center.y - groupBounds.center.y) > 1.2f) continue;   // 不在同一层
                float distance = Flat(bounds.center, groupBounds.center);
                if (distance > NightstandReach) continue;

                // “地上那块”：被床压住的看不见，玩家不会这么形容，给它加罚分只作兜底候选。
                float score = distance + (OverlapsFlat(bounds, bedBounds) ? 1.2f : 0f);
                if (score >= bestScore) continue;

                bestScore = score;
                bestBlock = piece.gameObject;
                bestDrawer = drawer;
            }
        }

        if (!bestBlock || !bestDrawer)
        {
            Debug.Log("[积木谜题] 床边没有需要收进抽屉的积木，保持原样。");
            return false;
        }

        Bounds drawerBounds = BoundsOf(bestDrawer.transform);
        Bounds visual = BoundsOf(bestBlock.transform);
        Vector3 anchor = bestBlock.transform.position;

        // 抽屉的开口方向：往外推 1cm 就贴着抽屉口了（推多了会挂在抽屉外面飘着）。
        Vector3 outward = bestDrawer.PickOutwardByClearance();
        outward = Vector3.ProjectOnPlane(outward, Vector3.up);
        if (outward.sqrMagnitude < .0004f) outward = Vector3.forward;
        outward.Normalize();

        // 抽屉网格的包围盒中心就是抽屉箱体的中心（面板薄的话它就是面板平面），
        // 积木放在这里 = 落在抽屉里；再往下沉就会陷进抽屉底板。
        Vector3 flat = drawerBounds.center + outward * .01f;

        // 积木模型的轴心常常不在几何中心（block 模型每块都有很大的局部偏移），
        // 所以按“视觉中心 → 轴心”的偏移反推，让积木底面略高于抽屉底板。
        float pivotToBottom = visual.min.y - anchor.y;
        Vector3 target = new Vector3(
            flat.x,
            drawerBounds.min.y + .022f - pivotToBottom,
            flat.z);

        bestBlock.transform.SetParent(bestDrawer.transform, true);
        bestBlock.transform.position = target;
        Physics.SyncTransforms();

        // 抽屉得是能拉的那种（DrawerSetup 可能因为“整件家具”把它禁用了），否则积木永远露不出来。
        bestDrawer.slide = true;
        if (!bestDrawer.enabled) bestDrawer.enabled = true;

        var stash = bestDrawer.GetComponent<StashedBlock>();
        if (!stash) stash = bestDrawer.gameObject.AddComponent<StashedBlock>();
        stash.Setup(bestDrawer, bestBlock);

        Debug.Log("[积木谜题] 床边的积木“" + bestBlock.name + "”已收进床头柜抽屉："
            + bestDrawer.name + "，抽屉中心 " + drawerBounds.center.ToString("F2")
            + "，抽屉尺寸 " + drawerBounds.size.ToString("F3")
            + "，开口方向 " + outward.ToString("F2")
            + "，积木位置 " + bestBlock.transform.position.ToString("F2"), bestDrawer);

        // 顺手把五块积木的位置打进日志：万一挑错了是哪一块，看这行就能确认。
        var parts = new List<string>();
        foreach (var piece in blocks)
            if (piece) parts.Add(piece.name + " " + piece.position.ToString("F2"));
        Debug.Log("[积木谜题] 五块积木位置：" + string.Join("；", parts.ToArray()));
        return true;
    }

    /// <summary>场景里所有床头柜抽屉面板（网格名 SM_BedTable_A1_Cab）。</summary>
    static List<FurnitureOpenable> CollectNightstandDrawers()
    {
        var found = new List<FurnitureOpenable>();
        foreach (var renderer in Object.FindObjectsOfType<MeshRenderer>())
        {
            if (!renderer) continue;
            var filter = renderer.GetComponent<MeshFilter>();
            if (!filter || !filter.sharedMesh) continue;
            if (!filter.sharedMesh.name.StartsWith(NightstandMeshPrefix, System.StringComparison.OrdinalIgnoreCase)) continue;

            var openable = renderer.GetComponent<FurnitureOpenable>();
            if (!openable) openable = renderer.GetComponentInParent<FurnitureOpenable>();
            if (!openable)
            {
                // DrawerSetup 和积木装配都在场景加载后跑，先后顺序不确定：先补一个，
                // 它之后看到已经有 FurnitureOpenable 会跳过这一块，不会重复挂。
                openable = renderer.gameObject.AddComponent<FurnitureOpenable>();
                openable.slide = true;
                openable.speed = 2.6f;
                openable.travel = .26f;
                openable.slideDirection = Vector3.forward;
            }
            if (openable && !found.Contains(openable)) found.Add(openable);
        }
        return found;
    }

    /// <summary>按“水平距离近”把抽屉归到各自的床头柜上（同一只柜子两层抽屉）。</summary>
    static List<List<FurnitureOpenable>> GroupByNightstand(List<FurnitureOpenable> drawers)
    {
        var groups = new List<List<FurnitureOpenable>>();
        foreach (var drawer in drawers)
        {
            Bounds bounds = BoundsOf(drawer.transform);
            List<FurnitureOpenable> home = null;
            foreach (var group in groups)
                if (Flat(BoundsOf(group[0].transform).center, bounds.center) <= GroupDistance) { home = group; break; }
            if (home == null) { home = new List<FurnitureOpenable>(); groups.Add(home); }
            home.Add(drawer);
        }
        return groups;
    }

    /// <summary>两层抽屉里取上面那层：拉开后更容易看见积木。</summary>
    static FurnitureOpenable TopDrawer(List<FurnitureOpenable> group)
    {
        FurnitureOpenable best = null;
        float bestY = float.MinValue;
        foreach (var drawer in group)
        {
            if (!drawer) continue;
            float y = BoundsOf(drawer.transform).center.y;
            if (y <= bestY) continue;
            bestY = y;
            best = drawer;
        }
        return best;
    }

    static float Flat(Vector3 a, Vector3 b)
    {
        a.y = 0f; b.y = 0f;
        return Vector3.Distance(a, b);
    }

    /// <summary>两个包围盒在水平面上有没有重叠（含一点点余量）。</summary>
    static bool OverlapsFlat(Bounds a, Bounds b)
    {
        const float margin = .02f;
        return a.min.x < b.max.x - margin && a.max.x > b.min.x + margin
            && a.min.z < b.max.z - margin && a.max.z > b.min.z + margin;
    }

    /// <summary>离这只床头柜最近的那张床的包围盒（用来判断积木是不是压在床底下）。</summary>
    static Bounds NearestBedBounds(Vector3 point)
    {
        Bounds best = new Bounds(point, Vector3.zero);
        float bestDistance = float.MaxValue;
        foreach (var bed in Object.FindObjectsOfType<Transform>())
        {
            if (!bed || bed.name != BedName) continue;
            Bounds bounds = BoundsOf(bed);
            float distance = Flat(bounds.center, point);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = bounds;
        }
        return best;
    }

    static Bounds BoundsOf(Transform target)
    {
        if (!target) return new Bounds(Vector3.zero, Vector3.one * .2f);
        var renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(target.position, Vector3.one * .2f);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            if (renderers[i]) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }
}
