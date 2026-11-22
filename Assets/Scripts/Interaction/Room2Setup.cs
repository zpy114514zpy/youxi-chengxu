using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 二楼二号房间的运行时装配：
/// ① 二号房门（走廊里那扇 BP_Door_Inside_A2）上锁：积木谜题没解开不能开，解开后需要钥匙；
/// ② 把电视柜第二层抽屉里的 key(1) 做成可以按 E 拾取的钥匙（位置不动，就在抽屉里）；
/// ③ 房间里的小房间门（BP_Door_Inside_A7）先锁住，等整理谜题做完自动打开。
/// 不改场景文件。
/// </summary>
public static class Room2Setup
{
    const string KeyName = "key(1)";
    const string RoomDoorName = "BP_Door_Inside_A2";
    const string SmallDoorName = "BP_Door_Inside_A7";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Setup()
    {
        if (!GameObject.Find("BP_Door_Inside_A5")) return;   // 只在这栋二层住宅里装配

        Transform roomDoor = FindByName(RoomDoorName);
        Transform smallDoor = FindByName(SmallDoorName);

        LockRoomDoor(roomDoor);
        LockSmallDoor(smallDoor);
        SetupKey();
        Room2Tidy.Build(smallDoor);
    }

    static Transform FindByName(string name)
    {
        foreach (var t in AllTransforms())
            if (t.name == name && t.GetComponent<InteractiveDoor>())
                return t;
        return null;
    }

    static void LockRoomDoor(Transform door)
    {
        if (!door) { Debug.LogWarning("[二号房间] 没找到 " + RoomDoorName); return; }
        var gate = door.GetComponent<DoorLock>();
        if (!gate) gate = door.gameObject.AddComponent<DoorLock>();
        gate.requireBlockPuzzle = true;
        gate.requiredItemId = "room2_key";
        gate.requireRoom2Tidy = false;
        gate.startsRoom2Tidy = true;
        gate.lockedMessage = "门锁着，好像现在还不到时候。";
        gate.needKeyMessage = "门锁着，得先找到钥匙。";
        gate.unlockMessage = "";   // 开门后直接接「二楼二号房间」，不额外插一条开门提示
        Debug.Log("[二号房间] 门锁已挂到 " + door.name, door);
    }

    static void LockSmallDoor(Transform door)
    {
        if (!door) { Debug.LogWarning("[二号房间] 没找到小房间门 " + SmallDoorName); return; }
        var gate = door.GetComponent<DoorLock>();
        if (!gate) gate = door.gameObject.AddComponent<DoorLock>();
        gate.requireBlockPuzzle = false;      // 只要求整理完成
        gate.requiredItemId = "";
        gate.requireRoom2Tidy = true;
        gate.startsRoom2Tidy = false;
        gate.tidyLockedMessage = "这扇门打不开，得先把房间整理干净。";
        Debug.Log("[二号房间] 小房间门已上锁 " + door.name, door);
    }

    /// <summary>电视柜第二层抽屉里的钥匙：位置完全不动，只补交互。</summary>
    static void SetupKey()
    {
        Transform key = null;
        foreach (var transform in AllTransforms())
        {
            if (transform.name == KeyName) { key = transform; break; }
        }
        if (!key) { Debug.LogWarning("[二楼二号房间] 场景里没找到 " + KeyName); return; }

        var renderers = key.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        var pickup = key.GetComponent<PickupItem>();
        if (!pickup) pickup = key.gameObject.AddComponent<PickupItem>();
        pickup.displayName = "二楼二号房间的钥匙";
        pickup.storyId = "room2_key";
        pickup.itemType = "钥匙";
        pickup.description = "在电视柜第二层抽屉里找到的黄铜钥匙，齿纹和二楼二号房间的门锁对得上。";

        if (!key.GetComponent<Collider>())
        {
            var box = key.gameObject.AddComponent<BoxCollider>();
            Vector3 size = bounds.size;
            Vector3 scale = key.lossyScale;
            // 钥匙本身只有几厘米，碰撞盒给到 8cm，玩家好瞄准；不要大到穿出抽屉。
            box.center = key.InverseTransformPoint(bounds.center);
            box.size = new Vector3(
                Mathf.Clamp(size.x, .06f, .10f) / Mathf.Max(.0001f, Mathf.Abs(scale.x)),
                Mathf.Clamp(size.y, .03f, .06f) / Mathf.Max(.0001f, Mathf.Abs(scale.y)),
                Mathf.Clamp(size.z, .04f, .08f) / Mathf.Max(.0001f, Mathf.Abs(scale.z)));
        }

        // 描边 shader 是加色混合（Blend SrcAlpha One），再叠 URP 的 Bloom，
        // 钥匙这种几厘米的小物件会被糊成一团青光，完全看不出是钥匙。先去掉描边。
        var outline = key.GetComponent<ProximityOutline>();
        if (outline) Object.Destroy(outline);

        // 钥匙材质如果是 Unreal 导过来的自发光，也会在暗处烧成一团。把自发光关掉。
        foreach (var r in key.GetComponentsInChildren<Renderer>(true))
        {
            foreach (var mat in r.materials)
            {
                if (!mat) continue;
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.black);
                if (mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", null);
                mat.DisableKeyword("_EMISSION");
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
        }

        // 也不要在钥匙边上点灯：点光按平方反比衰减，放在 14cm 处几乎必然过曝成白球。
        // 只留一盏极弱的小灯（0.06）当“这里有东西”的暗示，形状还是靠场景光。
        if (!key.Find("钥匙微光"))
        {
            var lampHost = new GameObject("钥匙微光");
            lampHost.transform.SetParent(key, false);
            lampHost.transform.localPosition = Vector3.up * .30f;
            var lamp = lampHost.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = new Color(1f, .9f, .7f);
            lamp.range = .38f;
            lamp.intensity = .06f;
            lamp.shadows = LightShadows.None;
        }

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
