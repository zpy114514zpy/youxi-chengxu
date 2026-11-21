using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 二楼二号房间的“整理与归位”谜题（第四个任务）。
/// 玩家用钥匙打开二号房门后开始：弹出三段目标提示 → 椅子 / 相册 / 笔记本电脑 / 茶杯
/// 四件东西上暗蓝色提示光 → 全部归位后小房间门自动打开 → 走进小房间触发最终独白。
/// 全部在运行时装配，不改场景文件。
/// </summary>
[DisallowMultipleComponent]
public sealed class Room2Tidy : MonoBehaviour
{
    public static Room2Tidy Instance { get; private set; }

    public sealed class Item
    {
        public string id;
        public string label;
        public Transform target;
        public Vector3 position;
        public Quaternion rotation;
        public bool placed;
        public GameObject glow;
        public bool pushOnly;
    }

    readonly List<Item> items = new List<Item>();
    Transform smallDoor;
    Vector3 smallDoorCheck;
    bool begun, finished, entered;

    public bool Solved => finished;

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    // ---------------- 装配 ----------------

    public static Room2Tidy Build(Transform smallRoomDoor)
    {
        if (Instance) return Instance;
        var host = new GameObject("房间整理谜题");
        var puzzle = host.AddComponent<Room2Tidy>();
        puzzle.Setup(smallRoomDoor);
        return puzzle;
    }

    void Setup(Transform smallRoomDoor)
    {
        smallDoor = smallRoomDoor;
        if (smallDoor) smallDoorCheck = smallDoor.position;

        // ① 椅子：推动归位
        var chair = FindNear("SM_OfficeChair_A1", new Vector3(-18.3f, 3.2f, -103.3f), 2.5f);
        if (chair)
        {
            Add("chair", "椅子", chair, new Vector3(-18.3252f, 3.2000f, -105.738f), Quaternion.identity, true);
            PreparePush(chair);
        }

        // ② 相册：场景里没有现成的，用一个相框网格复制一份，先丢在沙发边的地上
        var photo = CreatePhoto(new Vector3(-21.35f, 3.212f, -102.30f));
        if (photo)
        {
            Add("photo", "相册", photo.transform, new Vector3(-16.5580f, 4.3722f, -104.7070f), Quaternion.identity, false);
            PreparePickup(photo.transform);
        }

        // ③ 笔记本电脑
        var laptop = FindNear("SM_Laptop_A3", new Vector3(-17.0f, 3.2f, -102.8f), 2.5f);
        if (laptop)
        {
            Add("laptop", "笔记本电脑", laptop, new Vector3(-17.0198f, 3.9819f, -105.4350f), Quaternion.Euler(0f, 180f, 0f), false);
            PreparePickup(laptop);
        }

        // ④ 茶杯
        var mug = FindNear("SM_Mug_A15", new Vector3(-20.0f, 3.4f, -103.5f), 2.5f);
        if (mug)
        {
            Add("mug", "茶杯", mug, new Vector3(-17.0680f, 3.9819f, -105.9070f), Quaternion.identity, false);
            PreparePickup(mug);
        }

        // 三个“放置点”交互盒
        foreach (var item in items)
            if (!item.pushOnly) CreateSpot(item);

        Debug.Log("[整理谜题] 已装配 " + items.Count + " 件待归位物品。", this);
    }

    void Add(string id, string label, Transform target, Vector3 position, Quaternion rotation, bool pushOnly)
    {
        items.Add(new Item
        {
            id = id, label = label, target = target, position = position,
            rotation = rotation, pushOnly = pushOnly
        });
    }

    Item Find(string id)
    {
        foreach (var item in items) if (item.id == id) return item;
        return null;
    }

    public bool IsPlaced(string id)
    {
        var item = Find(id);
        return item == null || item.placed;
    }

    // ---------------- 物品准备 ----------------

    void PreparePickup(Transform t)
    {
        var pickup = t.GetComponent<PickupItem>();
        if (!pickup) pickup = t.gameObject.AddComponent<PickupItem>();
        var item = CurrentOf(t);
        pickup.displayName = item != null ? item.label : t.name;
        pickup.storyId = "tidy_" + (item != null ? item.id : t.name);
        pickup.itemType = "房间摆设";
        pickup.description = "二楼二号房间里需要归位的东西。";

        EnsureCollider(t);
    }

    void PreparePush(Transform t)
    {
        var body = t.GetComponent<Rigidbody>();
        if (!body) body = t.gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        if (!t.GetComponent<PushProp>()) t.gameObject.AddComponent<PushProp>();
        EnsureCollider(t);
    }

    static void EnsureCollider(Transform t)
    {
        if (t.GetComponent<Collider>() || t.GetComponentInChildren<Collider>()) return;
        var renderers = t.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        var box = t.gameObject.AddComponent<BoxCollider>();
        Vector3 scale = t.lossyScale;
        box.center = t.InverseTransformPoint(bounds.center);
        box.size = new Vector3(
            Mathf.Max(.05f, bounds.size.x) / Mathf.Max(.0001f, Mathf.Abs(scale.x)),
            Mathf.Max(.05f, bounds.size.y) / Mathf.Max(.0001f, Mathf.Abs(scale.y)),
            Mathf.Max(.05f, bounds.size.z) / Mathf.Max(.0001f, Mathf.Abs(scale.z)));
    }

    Item CurrentOf(Transform t)
    {
        foreach (var item in items) if (item.target == t) return item;
        return null;
    }

    static Transform FindNear(string name, Vector3 near, float radius)
    {
        Transform best = null;
        float bestDistance = radius;
        foreach (var t in AllTransforms())
        {
            if (t.name != name) continue;
            if (t.GetComponentsInChildren<Renderer>().Length == 0) continue;
            float distance = Vector3.Distance(t.position, near);
            if (best && distance >= bestDistance) continue;
            bestDistance = distance;
            best = t;
        }
        return best;
    }

    static Transform CreatePhoto(Vector3 position)
    {
        MeshFilter source = null;
        foreach (var filter in Object.FindObjectsOfType<MeshFilter>())
        {
            if (!filter.sharedMesh) continue;
            string mesh = filter.sharedMesh.name;
            if (mesh != "SM_Photo_A4B" && mesh != "SM_Photo_A4" && mesh != "SM_Photo_A5") continue;
            source = filter;
            if (mesh == "SM_Photo_A4B") break;
        }
        if (!source)
        {
            Debug.LogWarning("[整理谜题] 场景里没找到相框网格，相册这件先跳过。");
            return null;
        }

        var clone = Instantiate(source.gameObject, null, true);
        clone.name = "相册";
        clone.transform.position = position;
        clone.transform.rotation = Quaternion.identity;
        foreach (var outline in clone.GetComponentsInChildren<ProximityOutline>(true)) Destroy(outline);
        return clone.transform;
    }

    // ---------------- 提示光 ----------------

    static GameObject CreateGlow(Transform target, float height, float range)
    {
        var host = new GameObject("暗蓝提示光");
        host.transform.SetParent(target, false);
        host.transform.localPosition = new Vector3(0f, height, 0f);

        var light = host.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(.36f, .52f, 1f);
        light.range = range;
        light.intensity = .85f;
        light.shadows = LightShadows.None;
        return host;
    }

    // ---------------- 流程 ----------------

    /// <summary>用钥匙打开二号房门后调用。</summary>
    public void Begin()
    {
        if (begun) return;
        begun = true;

        var story = StoryProgress.Instance;
        if (story)
        {
            story.Enqueue("二楼二号房间", "里面的谜题好像和整理与归位有关。");
            story.Enqueue("房间整理谜题", "我希望家里永远是干干净净的，我需要把二楼二号房间整理干净。");
            story.Enqueue("温馨提醒", "需要把椅子、相册、笔记本电脑、茶杯归位。");
        }

        foreach (var item in items)
        {
            if (item.placed || !item.target) continue;
            float height = item.id == "chair" ? .55f : .12f;
            float range = item.id == "chair" ? 1.9f : 1.1f;
            item.glow = CreateGlow(item.target, height, range);
        }
    }

    void Update()
    {
        if (!begun) return;

        // 椅子：推到指定位置就算归位
        var chair = Find("chair");
        if (!finished && chair != null && !chair.placed && chair.target)
        {
            Vector3 a = chair.target.position, b = chair.position;
            a.y = b.y = 0f;
            if (Vector3.Distance(a, b) <= .55f)
            {
                chair.target.position = new Vector3(chair.position.x, chair.position.y, chair.position.z);
                chair.target.rotation = chair.rotation;
                var push = chair.target.GetComponent<PushProp>();
                if (push) push.enabled = false;
                MarkPlaced(chair);
            }
        }

        // 走进小房间
        if (finished && !entered && smallDoor)
        {
            var player = FindObjectOfType<PlayerController>();
            if (player)
            {
                Vector3 p = player.transform.position;
                if (p.y > 3.0f && p.y < 5.0f && p.z < smallDoorCheck.z + .55f
                    && Mathf.Abs(p.x - smallDoorCheck.x) < 1.6f)
                {
                    entered = true;
                    StoryProgress.Instance?.EnqueueCenter("内心独白",
                        "突然想去一楼客厅那里的沙发坐一会了，小时候最喜欢去沙发那个卧着看电视了。");
                }
            }
        }
    }

    /// <summary>玩家在放置点按 E。</summary>
    public bool PlaceFromSpot(string id, PlayerInventory inventory)
    {
        var item = Find(id);
        if (item == null || item.placed || !item.target || !inventory) return false;

        var pickup = inventory.TakeItem("tidy_" + id);
        if (!pickup) return false;

        pickup.gameObject.SetActive(true);
        pickup.transform.SetPositionAndRotation(item.position, item.rotation);
        foreach (var outline in pickup.GetComponentsInChildren<ProximityOutline>(true)) outline.enabled = false;
        var pick = pickup.GetComponent<PickupItem>();
        if (pick) pick.enabled = false;

        MarkPlaced(item);
        return true;
    }

    void MarkPlaced(Item item)
    {
        if (item.placed) return;
        item.placed = true;
        if (item.glow) Destroy(item.glow);

        StoryProgress.Instance?.EnqueueCenter("归位", item.label + "已归位");

        foreach (var other in items)
            if (!other.placed) return;

        Finish();
    }

    void Finish()
    {
        if (finished) return;
        finished = true;
        StoryProgress.Instance?.EnqueueCenter("整理完成", "房间收拾干净了，小房间的门开了。");

        if (smallDoor)
        {
            var gate = smallDoor.GetComponent<DoorLock>();
            if (gate) gate.UnlockSilently();
            var door = smallDoor.GetComponent<InteractiveDoor>();
            if (door)
            {
                var player = FindObjectOfType<PlayerController>();
                if (!door.IsOpen) door.Toggle(player ? player.transform.position : smallDoor.position + smallDoor.forward);
            }
        }
        StoryProgress.Instance?.EnqueueCenter("小房间", "小房间的门开了，进去看看吧。");
    }

    // ---------------- 工具 ----------------

    void CreateSpot(Item item)
    {
        var host = new GameObject("放置点-" + item.label);
        host.transform.position = item.position + Vector3.up * .04f;
        var box = host.AddComponent<BoxCollider>();
        box.size = new Vector3(.30f, .26f, .30f);
        box.isTrigger = false;
        var spot = host.AddComponent<TidySpot>();
        spot.puzzle = this;
        spot.itemId = item.id;
        spot.label = item.label;
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
