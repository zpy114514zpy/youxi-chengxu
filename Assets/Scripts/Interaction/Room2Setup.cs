using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 二楼二号房间的运行时装配：
/// ① 二号房门（走廊里那扇 BP_Door_Inside_A2）上锁：只要拿到电视柜抽屉里的钥匙就能开，
///    不再要求先做完积木谜题（那套顺序只管一号房间的 2 号门 A6）；
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
        // 这扇门只认钥匙：积木谜题是通往一号房间 2 号门（A6）的前置，不该拦这里。
        // 之前写成 true，玩家在走廊柜子里拿到钥匙、还没做积木谜题时门就打不开。
        gate.requireBlockPuzzle = false;
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

        // 钥匙材质如果是 Unreal 导过来的自发光，也会在暗处烧成一团。把自发光关掉，
        // 提示全部交给 DrawerItemHint —— 只有一圈蓝色描边，不点灯，
        // 所以抽屉关着时完全看不出这里藏着东西。
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

        // 场景 / 预制体里若还残留「钥匙微光」子物体，先清掉：
        // 点光按平方反比衰减，14cm 处 intensity .5 会直接烧成白球，降到 .06 又完全看不见。
        var oldLamp = key.Find("钥匙微光");
        if (oldLamp) Object.Destroy(oldLamp.gameObject);

        // 钥匙就摆在抽屉里，DrawerItemHint 只负责“藏 / 露 + 提示”：
        // 抽屉关着时关掉渲染器和碰撞体（不漏光、也不能隔空捡），
        // 一拉开钥匙就跟着抽屉一起滑出来，露出一圈蓝色描边。不点灯，不会提前剧透。
        var hint = key.GetComponent<DrawerItemHint>();
        if (!hint) hint = key.gameObject.AddComponent<DrawerItemHint>();
        hint.Setup();

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
