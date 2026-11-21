using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 让屋子里“本来就有抽屉模型、但没有交互”的抽屉都能按 E 拉开（向外拉，不是往里推）。
/// 场景里有一批空的“书桌抽屉 N”组件挂在没网格、没碰撞体的空物体上，按了没反应，
/// 这里把它们清掉，改成挂到真正的抽屉网格上。
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
        int fixedPanels = SlideWidePanels();
        Debug.Log("[抽屉] 新装交互 " + added + " 个，改成向外平移 " + fixedPanels + " 个。");
    }

    /// <summary>
    /// 宽而矮的面板（抽屉面板）如果被配成了“旋转柜门”，改成向外平移：
    /// 抽屉只能是拉出来的，旋转开会穿进柜体。
    /// </summary>
    static int SlideWidePanels()
    {
        int fixedCount = 0;
        foreach (var openable in Object.FindObjectsOfType<FurnitureOpenable>())
        {
            if (!openable) continue;
            // 二楼走廊那组电视柜（6 抽屉 + 2 柜门）统一按“拉出来”处理
            bool shelfPanel = openable.name.Contains("SM_Shelf_TV_A1_Door");
            if (!shelfPanel && openable.slide) continue;
            var renderer = openable.GetComponent<Renderer>();
            if (!renderer) renderer = openable.GetComponentInChildren<Renderer>();
            if (!renderer) continue;
            Vector3 size = renderer.bounds.size;
            float width = Mathf.Max(size.x, size.z);
            if (!shelfPanel && (size.y > .55f || width < size.y * 1.5f)) continue;   // 高面板是真的柜门，不动
            openable.slide = true;
            openable.travel = shelfPanel ? .26f : Mathf.Clamp(Mathf.Min(size.x, size.z) + .12f, .20f, .38f);
            openable.speed = 2.4f;
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
            var mesh = renderer.GetComponent<MeshFilter>() ? renderer.GetComponent<MeshFilter>().sharedMesh : null;
            string meshName = mesh ? mesh.name : "";
            if (!IsDrawerMesh(meshName)) continue;

            var go = renderer.gameObject;
            if (go.GetComponentInParent<FurnitureOpenable>()) continue;   // 已经有交互了
            if (go.GetComponentInParent<EnvelopeFragment>()) continue;

            Bounds bounds = renderer.bounds;
            if (bounds.size.y > .55f || bounds.size.x > 1.2f || bounds.size.z > 1.2f) continue;  // 不像抽屉面板

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
            openable.speed = 2.4f;
            openable.travel = Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z) + .10f, .22f, .38f);
            openable.slideDirection = Vector3.forward;    // 真实方向由 FurnitureOpenable 按柜体几何算
            added++;
        }
        return added;
    }

    /// <summary>SM_Drawer_A1 的抽屉面板、办公桌（两张并排 = 8 个抽屉）的 Cab 面板，学生桌的 Draw N_low 抽屉盒，以及电视柜的 6 抽屉 + 2 柜门。</summary>
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
