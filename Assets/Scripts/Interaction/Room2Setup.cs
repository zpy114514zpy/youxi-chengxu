using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 二楼二号房间的运行时装配：
/// ① 给二号门加门锁（积木谜题没解开不能开，解开后需要钥匙）；
/// ② 把走廊里的 key(1) 做成可以按 E 拾取的钥匙。
/// 不改场景文件。
/// </summary>
public static class Room2Setup
{
    const string KeyName = "key(1)";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Setup()
    {
        if (!GameObject.Find("BP_Door_Inside_A5")) return;   // 只在这栋二层住宅里装配
        LockSecondRoomDoor();
        SetupKey();
    }

    static void LockSecondRoomDoor()
    {
        foreach (var door in Object.FindObjectsOfType<InteractiveDoor>())
        {
            var intro = door.GetComponent<DoorRoomIntro>();
            if (!intro || intro.doorNumber != 2) continue;
            if (!door.GetComponent<DoorLock>()) door.gameObject.AddComponent<DoorLock>();
            Debug.Log("[二号房间] 门锁已挂到 " + door.name, door);
            return;
        }
        Debug.LogWarning("[二号房间] 没找到 doorNumber=2 的门。");
    }

    static void SetupKey()
    {
        Transform key = null;
        foreach (var transform in AllTransforms())
        {
            if (transform.name == KeyName) { key = transform; break; }
        }
        if (!key) { Debug.LogWarning("[二楼二号房间] 场景里没找到 " + KeyName); return; }

        // 钥匙模型可能悬在半空：按脚下的地面把它放平。
        var renderers = key.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        float floor = bounds.min.y;
        foreach (var hit in Physics.RaycastAll(new Vector3(bounds.center.x, bounds.center.y + .3f, bounds.center.z),
                     Vector3.down, 1.2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (hit.point.y > 3.0f && hit.point.y < 3.35f) { floor = hit.point.y; break; }
        if (bounds.min.y - floor > .02f)
            key.position += Vector3.up * (floor + .006f - bounds.min.y);

        var pickup = key.GetComponent<PickupItem>();
        if (!pickup) pickup = key.gameObject.AddComponent<PickupItem>();
        pickup.displayName = "二楼二号房间的钥匙";
        pickup.storyId = "room2_key";
        pickup.itemType = "钥匙";
        pickup.description = "在走廊角落里捡到的黄铜钥匙，齿纹和二楼二号房间的门锁对得上。";

        if (!key.GetComponent<Collider>())
        {
            var box = key.gameObject.AddComponent<BoxCollider>();
            Vector3 size = bounds.size;
            Vector3 scale = key.lossyScale;
            box.center = key.InverseTransformPoint(bounds.center);
            box.size = new Vector3(
                Mathf.Max(.10f, size.x) / Mathf.Max(.0001f, Mathf.Abs(scale.x)),
                Mathf.Max(.06f, size.y) / Mathf.Max(.0001f, Mathf.Abs(scale.y)),
                Mathf.Max(.10f, size.z) / Mathf.Max(.0001f, Mathf.Abs(scale.z)));
        }

        var outline = key.GetComponent<ProximityOutline>();
        if (!outline) outline = key.gameObject.AddComponent<ProximityOutline>();
        outline.revealDistance = 3.2f;
        outline.outlineWidth = .006f;
        outline.outlineColor = new Color(1f, .84f, .35f, .95f);

        Debug.Log("[二楼二号房间] 钥匙已可拾取，位置 " + key.position.ToString("F3"), key);
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
