using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 让屋子里“本来就有抽屉/柜门模型、但没有交互”的抽屉都能按 E 拉开。
/// 场景里有一批空的“书桌抽屉 N”组件挂在没网格、没碰撞体的空物体上，按了没反应，
/// 这里把它们清掉，改成挂到真正的抽屉网格上。
///
/// 规则（用户明确要求）：**所有抽屉、所有柜门，一律向外拉出来**，不允许往里推、也不允许旋转开。
/// 运行时装配，不改场景文件。
/// </summary>
public static class DrawerSetup
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Setup()
    {
        if (!GameObject.Find("BP_Door_Inside_A5")) return;
        RemoveBrokenDrawers();
        int added = AttachToDrawerMeshes();
        int fixedPanels = MakeAllSlide();
        Debug.Log("[抽屉] 新装交互 " + added + " 个，改成向外拉出 " + fixedPanels + " 个。");
    }

    /// <summary>
    /// 所有面板统一改成平移拉出。
    /// travel 按面板类型给：抽屉盒（最薄的是高度）按深度的 85% 拉出，柜门固定 0.26m。
    /// </summary>
    static int MakeAllSlide()
    {
        int fixedCount = 0;
        foreach (var openable in Object.FindObjectsOfType<FurnitureOpenable>())
        {
            if (!openable) continue;
            var renderer = openable.GetComponent<Renderer>();
            if (!renderer) renderer = openable.GetComponentInChildren<Renderer>();
            if (!renderer) continue;

            Vector3 size = renderer.bounds.size;
            bool drawerBox = size.y <= size.x && size.y <= size.z;   // 最薄的是高度 → 抽屉盒
            float minHorizontal = Mathf.Min(size.x, size.z);

            openable.slide = true;
            openable.speed = 2.6f;
            openable.travel = drawerBox
                ? Mathf.Clamp(minHorizontal * .85f, .16f, .30f)
                : .26f;
            fixedCount++;
        }
        return fixedCount;
    }

    /// <summary>清掉挂在空物体上的无效抽屉交互（书桌抽屉 1~10 那些）。</summary>
    static void RemoveBrokenDrawers()
    {
        var list = new List<FurnitureOpenable>(Object.FindObjectsOfType<FurnitureOpenable>());
        foreach (var openable in list)
        {
            if (!openable) continue;
            if (openable.GetComponent<Renderer>() || openable.GetComponent<Collider>()) continue;
            if (openable.GetComponentsInChildren<Renderer>(true).Length > 0) continue;
            if (openable.GetComponentsInChildren<Collider>(true).Length > 0) continue;
            Object.Destroy(openable);
        }
    }

    static int AttachToDrawerMeshes()
    {
        int added = 0;
        foreach (var renderer in Object.FindObjectsOfType<MeshRenderer>())
        {
            if (!renderer) continue;
            var filter = renderer.GetComponent<MeshFilter>();
            string meshName = filter && filter.sharedMesh ? filter.sharedMesh.name : "";
            if (!IsDrawerMesh(meshName)) continue;

            var go = renderer.gameObject;
            if (go.GetComponentInParent<FurnitureOpenable>()) continue;   // 已经有交互了
            if (go.GetComponentInParent<EnvelopeFragment>()) continue;

            Bounds bounds = renderer.bounds;
            if (bounds.size.y > .55f || bounds.size.x > 1.2f || bounds.size.z > 1.2f) continue;  // 不像抽屉

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
            openable.travel = .26f;
            openable.slideDirection = Vector3.forward;    // 真实方向由 FurnitureOpenable 按柜体几何算
            added++;
        }
        return added;
    }

    /// <summary>SM_Drawer_A1 的抽屉面板、办公桌的 Cab 面板、学生桌的 Draw N_low 抽屉盒、电视柜的 6 抽屉 + 2 柜门。</summary>
    static bool IsDrawerMesh(string name)
    {
        if (name.StartsWith("SM_Drawer_A1_Cab")) return true;
        if (name.StartsWith("SM_WorkingTable_A1_Cab")) return true;
        if (name.StartsWith("SM_WorkingTable_A2_Cab")) return true;
        if (name.StartsWith("SM_Shelf_TV_A1_Door")) return true;
        if (name.StartsWith("Draw ") && name.Contains("_low")) return true;
        return false;
    }
}
