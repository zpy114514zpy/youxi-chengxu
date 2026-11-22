using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 谜题二（积木块谜题）的运行时装配：不改场景文件。
/// 找到场景里那一组“散落的积木”（block 模型实例下的五块），
/// 在二楼一号房间 1 号门口生成积木堆，并把第二块信封碎片 Line002 藏起来等解密后出现。
/// 场景里没有积木就什么都不做。
/// </summary>
public static class BlockPuzzleSetup
{
    /// <summary>场景里五块积木的节点名（都在 block 这个模型实例下面）。</summary>
    static readonly string[] BlockNames = { "Cone001", "Box002", "Box002 (1)", "Cylinder001", "Box003" };

    const string DoorName = "BP_Door_Inside_A5";
    const string FragmentName = "Line002";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Setup()
    {
        Transform root = FindBlockRoot();
        if (!root) return;                                  // 不是积木谜题的场景
        if (Object.FindObjectOfType<BlockPuzzle>()) return;  // 已经装配过

        var pieces = new List<Transform>();
        foreach (var name in BlockNames)
        {
            var piece = root.Find(name);
            if (piece) pieces.Add(piece);
            else Debug.LogWarning("[积木谜题] 场景里没有找到积木“" + name + "”。");
        }
        if (pieces.Count == 0) return;

        Transform fragment = FindFragment();
        if (fragment) fragment.gameObject.SetActive(false);

        var host = new GameObject("积木堆");
        var puzzle = host.AddComponent<BlockPuzzle>();
        puzzle.Build(GroundPosition(), pieces, fragment ? fragment.gameObject : null);

        Debug.Log("[积木谜题] 装配完成：积木 " + pieces.Count + " 块，积木堆 " + host.transform.position.ToString("F3")
            + (fragment ? "，碎片 " + fragment.name : "，未找到碎片 Line002"));
    }

    static Transform FindBlockRoot()
    {
        foreach (var transform in AllTransforms())
        {
            if (transform.name != "block") continue;
            // 同一个场景里可能还有别的叫 block 的东西，认准带积木子物体的那一个。
            if (transform.Find("Cone001") || transform.Find("Box002")) return transform;
        }
        return null;
    }

    static Transform FindFragment()
    {
        Transform best = null;
        float bestDistance = float.MaxValue;
        Transform cabinet = null;
        foreach (var transform in AllTransforms())
            if (!cabinet && transform.name.StartsWith("BP_ChildBed_A1") && transform.name.Contains("Cab1"))
                cabinet = transform;

        foreach (var transform in AllTransforms())
        {
            if (transform.name != FragmentName) continue;
            float distance = cabinet ? (transform.position - cabinet.position).sqrMagnitude : 0f;
            if (best && distance >= bestDistance) continue;
            bestDistance = distance;
            best = transform;
        }
        return best;
    }

    /// <summary>
    /// 积木堆摆在门口正对着的空地上（地面高度用射线量出来）。
    /// 两个硬条件：
    /// ① 离门轴 1.45m 以上——门扇绕门轴能扫到约 1m 的扇形，摆近了门会被积木堆卡住开不动；
    /// ② 落点必须是地面，不能是床/桌面（否则积木堆会悬在半空）。
    /// </summary>
    static Vector3 GroundPosition()
    {
        Vector3 fallback = new Vector3(-17.409f, 3.209f, -92.039f);
        Transform door = null;
        foreach (var transform in AllTransforms())
            if (transform.name == DoorName) { door = transform; break; }
        if (!door) return fallback;

        Vector3 hinge = door.position;
        var doorLogic = door.GetComponent<InteractiveDoor>();
        if (doorLogic) hinge = door.transform.TransformPoint(doorLogic.hingeLocal);

        // 先量门口内侧的地面高度：后面用它把“床面/桌面”这种假地面排除掉。
        Vector3 origin = door.position;
        if (!Physics.Raycast(new Vector3(origin.x + .45f, origin.y, origin.z - .5f), Vector3.down, out var probe,
                5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return fallback;
        float groundY = probe.point.y;

        // 从门口往前一步步试：越靠近门越好，找到第一块干净的空地就停下。
        // side 从 -1.15 起：原来的位置离左边那把单人沙发（SM_Chair_B1）只有 0.5m，
        // 把积木堆整体往右（-z）挪 1m，和沙发拉开距离，又不会挡住门扇。
        for (float forward = 1.25f; forward <= 2.6f; forward += .15f)
        {
            for (float side = -1.15f; side >= -2.1f; side -= .10f)
            {
                Vector3 spot = new Vector3(origin.x + forward, origin.y, origin.z + side);
                if (!Physics.Raycast(spot, Vector3.down, out var hit, 4f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    continue;
                if (Mathf.Abs(hit.point.y - groundY) > .06f) continue;      // 落在床/桌上，不是地面

                Vector3 flat = new Vector3(spot.x, 0f, spot.z);
                if (Vector3.Distance(flat, new Vector3(hinge.x, 0f, hinge.z)) < 1.45f) continue;

                var center = new Vector3(spot.x, hit.point.y + .1f, spot.z);
                bool blocked = false;
                foreach (var c in Physics.OverlapBox(center, new Vector3(.40f, .08f, .33f), Quaternion.identity,
                             Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    if (c.bounds.size.y > .12f) { blocked = true; break; }
                }
                if (blocked) continue;

                return new Vector3(spot.x, hit.point.y + .002f, spot.z);
            }
        }
        return fallback;
    }

    static List<Transform> AllTransforms()
    {
        var list = new List<Transform>();
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid()) return list;
        foreach (var root in scene.GetRootGameObjects())
            list.AddRange(root.GetComponentsInChildren<Transform>(true));
        return list;
    }
}
