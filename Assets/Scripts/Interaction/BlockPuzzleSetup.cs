using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 谜题三（积木块谜题）的运行时装配：不改场景文件。
/// 找到场景里那一组“散落的积木”（block 模型实例下的五块），
/// 在二楼一号房间 1 号门口生成积木堆，并把第三块信封碎片 Line002 藏起来等解密后出现。
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

    /// <summary>积木堆摆在一号门内侧的空地上（地面高度用射线量出来）。</summary>
    static Vector3 GroundPosition()
    {
        Vector3 fallback = new Vector3(-18.459f, 3.207f, -91.589f);
        Transform door = null;
        foreach (var transform in AllTransforms())
            if (transform.name == DoorName) { door = transform; break; }
        if (!door) return fallback;

        Vector3 probe = new Vector3(door.position.x + .5f, door.position.y, door.position.z - .9f);
        if (Physics.Raycast(probe, Vector3.down, out var hit, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return new Vector3(probe.x, hit.point.y + .002f, probe.z);
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
