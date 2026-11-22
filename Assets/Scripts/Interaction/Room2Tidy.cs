using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 二楼二号房间的“整理与归位”谜题（第四个任务）。
/// 玩家用钥匙打开二号房门后开始：弹出三段目标提示 → 椅子 / 相框 / 笔记本电脑 / 茶杯
/// 四件东西上暗蓝色提示光 → 全部归位后小房间门自动打开 →
/// 小房间书桌上的碎片④带一圈黄色描边，捡起后由 StoryProgress 收尾（恭喜 + 前往一楼客厅）。
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
    bool begun, finished;

    // 小房间里那张纸（信封碎片④）：只挂黄色描边，没有灯
    Transform roomFragment;

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

        // ② 相框：直接用场景里摆在沙发边地上的 SM_Photo_A48，不再复制网格另造一个
        var photo = FindSceneObject("SM_Photo_A48");
        if (!photo) photo = CreatePhoto(new Vector3(-21.35f, 3.212f, -102.30f));
        if (photo)
        {
            // 从空壳父级里拆出来：之后按世界坐标搬动、放置，不会被父级缩放带歪
            photo.SetParent(null, true);
            var photoSpot = DeskRightSpot(photo, out var photoFacing);
            Add("photo", "相框", photo, photoSpot, photoFacing, false);
            PreparePickup(photo, .22f);
        }
        else Debug.LogWarning("[整理谜题] 没找到场景里的 SM_Photo_A4，相册这件跳过。", this);

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

        // 四件东西统一保证有蓝色描边（相框是场景里的普通摆件，原本没有描边；
        // 椅子 / 笔记本 / 茶杯身上已经带着场景里的描边，这里不重复改它们的参数）。
        foreach (var item in items) EnsureOutline(item.target);
        if (photo) EnsureOutline(photo, true);   // 相框强制刷成和其它三个一样的蓝色描边

        // 三个“放置点”交互盒
        foreach (var item in items)
            if (!item.pushOnly) CreateSpot(item);

        // 小房间书桌上那张纸：整理完成后指引玩家来捡
        roomFragment = DrawerFragmentReveal.FindDeep("对象005");
        PrepareRoomFragment();

        Debug.Log("[整理谜题] 已装配 " + items.Count + " 件待归位物品；小房间碎片=" + (roomFragment ? roomFragment.name : "无"), this);
    }

    /// <summary>把小房间里那张纸变成可拾取的信封碎片④。</summary>
    void PrepareRoomFragment()
    {
        if (!roomFragment) { Debug.LogWarning("[整理谜题] 没找到小房间里的信封碎片（对象005）。"); return; }

        var existing = roomFragment.GetComponent<PickupItem>();
        if (existing && !string.IsNullOrEmpty(existing.storyId)) return;   // 已被别的谜题占用

        var pick = existing ? existing : roomFragment.gameObject.AddComponent<PickupItem>();
        pick.displayName = "信封碎片④";
        pick.storyId = "letter_fragment_4";
        pick.itemType = "信封碎片";
        pick.description = "在小房间书桌上找到的一页信，边角被摩挲得有点毛。";

        // 碎片④ 自己的名字和内容：不写这两行的话会沿用 EnvelopeFragment 的默认值，
        // 捡起来会被当成「信封碎片①」，还会再把“去 1 号门”的目标弹一遍。
        var fragment = roomFragment.GetComponent<EnvelopeFragment>();
        if (!fragment) fragment = roomFragment.gameObject.AddComponent<EnvelopeFragment>();
        fragment.fragmentName = "信封碎片④";
        fragment.text = "我希望家里永远是干干净净的！";

        EnsureCollider(roomFragment, .18f, .06f);
        // 只要一圈黄色描边，不打任何点光：之前那盏“碎片指引光”在书架格里烧成一团白，
        // 纸面内容完全看不见。
        EnsureFragmentOutline(roomFragment);
    }

    /// <summary>碎片（小房间里那张纸）的黄色描边。</summary>
    static void EnsureFragmentOutline(Transform target)
    {
        if (!target) return;
        var outline = target.GetComponent<ProximityOutline>();
        if (!outline) outline = target.GetComponentInChildren<ProximityOutline>(true);
        if (!outline) outline = target.gameObject.AddComponent<ProximityOutline>();
        outline.Apply(new Color(1f, .80f, .24f, .88f), .016f, 6f);
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

    void PreparePickup(Transform t, float minXZ = .05f)
    {
        var pickup = t.GetComponent<PickupItem>();
        if (!pickup) pickup = t.gameObject.AddComponent<PickupItem>();
        var item = CurrentOf(t);
        pickup.displayName = item != null ? item.label : t.name;
        pickup.storyId = "tidy_" + (item != null ? item.id : t.name);
        pickup.itemType = "房间摆设";
        pickup.description = "二楼二号房间里需要归位的东西。";

        EnsureCollider(t, minXZ, minXZ > .1f ? minXZ * .5f : .05f);

        var check = t.GetComponent<BoxCollider>();
        Vector3 sc = t.lossyScale;
        Debug.Log("[整理] " + t.name + " storyId=" + pickup.storyId
            + " BoxCollider=" + (check ? "有 world=" + (check.size.x * Mathf.Abs(sc.x)).ToString("F2") : "无")
            + " scale=" + sc.ToString("F3"));
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

    static void EnsureCollider(Transform t, float minXZ = .05f, float minY = .05f)
    {
        // 一定要挂在 PickupItem 所在的那个节点上：之前只依赖子网格自带的 MeshCollider，
        // 相册/茶杯这种又薄又小的东西射线一擦就过去了，玩家瞄着也捡不起来。
        var renderers = t.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) return;
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);

        var box = t.GetComponent<BoxCollider>();
        if (!box) box = t.gameObject.AddComponent<BoxCollider>();
        Vector3 scale = t.lossyScale;
        box.center = t.InverseTransformPoint(bounds.center);
        box.size = new Vector3(
            Mathf.Max(minXZ, bounds.size.x) / Mathf.Max(.0001f, Mathf.Abs(scale.x)),
            Mathf.Max(minY, bounds.size.y) / Mathf.Max(.0001f, Mathf.Abs(scale.y)),
            Mathf.Max(minXZ, bounds.size.z) / Mathf.Max(.0001f, Mathf.Abs(scale.z)));
        box.isTrigger = false;
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

    /// <summary>
    /// 用场景里现成的相框网格拼一个“掉落在地上的相册”。
    ///
    /// 不能直接 Instantiate 墙上的相框：那些相框是预制体实例的深层子物体，
    /// 把它们单独复制出来会丢掉父级的缩放 / 轴心偏移 —— 复制品要么小得看不见、
    /// 要么飞到别处去，而碰撞盒是按真实包围盒算的，于是玩家“看得见却点不到”。
    /// 这里改成只借 sharedMesh / sharedMaterials，自己搭一个干净的层级：
    ///   相册（根，PickupItem + BoxCollider + 描边）
    ///     └ frame（MeshFilter + MeshRenderer，本地原点压在网格中心）
    /// 这样根节点的位置就是相框的几何中心，碰撞盒、描边、拾取范围三者必然对齐。
    /// </summary>
    static Transform CreatePhoto(Vector3 position)
    {
        Mesh mesh = null;
        Material[] materials = null;
        foreach (var filter in Object.FindObjectsOfType<MeshFilter>())
        {
            if (!filter.sharedMesh) continue;
            string name = filter.sharedMesh.name;
            if (name != "SM_Photo_A4B" && name != "SM_Photo_A4" && name != "SM_Photo_A5") continue;
            var sourceRenderer = filter.GetComponent<MeshRenderer>();
            if (!sourceRenderer) continue;
            mesh = filter.sharedMesh;
            materials = sourceRenderer.sharedMaterials;
            if (name == "SM_Photo_A4B") break;
        }
        if (!mesh)
        {
            Debug.LogWarning("[整理谜题] 场景里没找到相框网格，相册这件先跳过。");
            return null;
        }

        var root = new GameObject("相册");
        root.layer = 0;                                   // 确保不会被“忽略射线”那一层藏掉
        root.transform.position = position;
        // 面朝上、稍微偏一点角度：像被人随手放在地上的相框。
        // 绕 X 转 -90°：网格的正面(+Z)朝上；转 +90° 会正面朝下、只看见背板。
        root.transform.rotation = Quaternion.Euler(-90f, 22f, 0f);

        // 有的 FBX 把顶点按厘米存，网格会大出 100 倍。超过 1.2m 就按比例缩回来，
        // 免得生出一个几十米大的“相册”，碰撞盒也大得离谱。
        float extent = Mathf.Max(mesh.bounds.size.x, Mathf.Max(mesh.bounds.size.y, mesh.bounds.size.z));
        float scale = extent > 1.2f ? 1.2f / extent : 1f;

        var visual = new GameObject("frame");
        visual.layer = 0;
        visual.transform.SetParent(root.transform, false);
        visual.transform.localRotation = Quaternion.identity;
        visual.transform.localScale = Vector3.one * scale;
        // mesh 的顶点中心常常不在原点：把网格中心挪到本地原点，
        // 根节点的 position 就等于相框的几何中心了。
        visual.transform.localPosition = -mesh.bounds.center * scale;

        var meshFilter = visual.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = mesh;
        var meshRenderer = visual.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterials = materials;

        // 落到地面上：从生成点上方往下打一条射线，把相框底面正好压在落地高度。
        // （这时候还没加碰撞体，射线只会打到地板，不会打到相框自己。）
        Bounds bounds = BoundsOf(root.transform);
        if (Physics.Raycast(position + Vector3.up * .6f, Vector3.down, out var ground, 1.6f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            root.transform.position += Vector3.up * (ground.point.y + .004f - bounds.min.y);

        Debug.Log("[整理谜题] 相册已生成在 " + root.transform.position.ToString("F2")
            + "，网格 " + mesh.name + "，尺寸 " + BoundsOf(root.transform).size.ToString("F3"));
        return root.transform;
    }

    static Bounds BoundsOf(Transform t)
    {
        var renderers = t.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(t.position, Vector3.zero);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    static Transform FindSceneObject(string name)
    {
        foreach (var t in AllTransforms())
            if (t.name == name) return t;
        return null;
    }

    /// <summary>场景里那张带显示器的大工作桌（靠北墙）。</summary>
    static Transform FindWorkingDesk()
    {
        Transform best = null;
        float bestDistance = 3f;
        var near = new Vector3(-18.28f, 3.99f, -106.5f);
        foreach (var t in AllTransforms())
        {
            if (t.name.IndexOf("WorkingTable_A1", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (t.GetComponentsInChildren<Renderer>().Length == 0) continue;
            var bounds = BoundsOf(t);
            if (bounds.size.x < 1f) continue;                 // 小边桌不参与
            float distance = Vector3.Distance(bounds.center, near);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = t;
        }
        return best;
    }

    /// <summary>
    /// 相框的归位点：工作桌的右侧。
    /// “右侧”按玩家站在桌前、面朝里（世界 -Z）来算 —— 这时右手边是世界 -X，
    /// 也就是桌子包围盒 min.x 那一端。高度用桌面实测值 + 相框自身“根节点到底边”的距离，
    /// 所以不论网格轴心在底边还是中心，放上去都正好立在桌面上。
    /// </summary>
    static Vector3 DeskRightSpot(Transform photo, out Quaternion rotation)
    {
        rotation = photo.rotation;                  // 沿用场景里的朝向，摆上桌不会突然翻面
        float lift = photo.position.y - BoundsOf(photo).min.y;
        if (lift < 0f || lift > 1.2f) lift = 0f;

        var desk = FindWorkingDesk();
        if (!desk)
        {
            Debug.LogWarning("[整理谜题] 没找到工作桌，相框归位点退回默认位置。");
            return new Vector3(-19.05f, 3.982f + lift + .004f, -106.45f);
        }

        var bounds = BoundsOf(desk);
        float top = bounds.max.y;
        float x = bounds.min.x + .24f;
        float z = bounds.center.z + .04f;
        for (int i = 0; i < 5; i++)
        {
            if (Physics.Raycast(new Vector3(x, top + .45f, z), Vector3.down, out var hit, 1.2f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && hit.normal.y > .6f)
            {
                top = hit.point.y;
                break;
            }
            x += .12f;                              // 打到桌沿外面了，往里挪一点再试
        }

        var spot = new Vector3(x, top + lift + .004f, z);
        Debug.Log("[整理谜题] 相框归位点 " + spot.ToString("F3") + "（工作桌 " + desk.name
            + "，包围盒 " + bounds.min.ToString("F2") + " ~ " + bounds.max.ToString("F2") + "）");
        return spot;
    }

    /// <summary>
    /// 保证目标身上有一圈蓝色描边（和场景里椅子 / 笔记本 / 茶杯自带的那套参数一致）。
    /// 注意：描边材质是在 OnEnable 里按当时的字段建的，加完必须走 Apply 才会用新颜色。
    /// force = true 时，即使身上已经挂着描边也把参数刷成这一套（相框用）。
    /// </summary>
    static void EnsureOutline(Transform target, bool force = false)
    {
        if (!target) return;
        var outline = target.GetComponent<ProximityOutline>();
        if (!outline) outline = target.GetComponentInChildren<ProximityOutline>(true);
        if (!outline)
        {
            outline = target.gameObject.AddComponent<ProximityOutline>();
            force = true;                          // 新加的组件材质还没建，必须 Apply 一次
        }
        else if (!force) return;                   // 椅子 / 笔记本 / 茶杯原本就有，别动它们的参数
        outline.Apply(new Color(.25f, .9f, 1f, .9f), .018f, 2.6f);
    }

    // ---------------- 提示光 ----------------

    /// <summary>
    /// 待归位物品的“暗蓝提示光”。
    /// 亮度/范围都压得很低：屋里本来就暗，一盏 0.85 / 1.9m 的点光会把沙发和地板
    /// 烧成一团白蓝色（相册那张截图就是），反而看不出东西的形状。
    /// 现在只负责“让物件周围不至于全黑”，找东西主要靠描边。
    /// </summary>
    static GameObject CreateGlow(Transform target, float height, float range)
    {
        var host = new GameObject("暗蓝提示光");
        host.transform.SetParent(target, false);
        // 目标可能被旋转过（相册是平放在地上的），用世界坐标放到正上方，
        // 否则 localPosition 会跟着旋转跑到旁边去。
        host.transform.position = target.position + Vector3.up * height;

        var light = host.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(.36f, .52f, 1f);
        light.range = range;
        light.intensity = .12f;
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
            story.Enqueue("温馨提醒", "需要把椅子、相框、笔记本电脑、茶杯归位。");
        }

        foreach (var item in items)
        {
            if (item.placed || !item.target) continue;
            float height = item.id == "chair" ? .55f : .12f;
            float range = item.id == "chair" ? .95f : .55f;
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

        // 走进小房间、捡走碎片之后的提示，交给 StoryProgress.AddFragment("信封碎片④") 排：
        // 先「碎片四」这一页，再「恭喜完成第四个解密任务」→「请前往一楼客厅…」。
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

        // 这里不再弹「谜题四完成」：收尾的“恭喜完成第四个解密任务 / 请前往一楼客厅”
        // 统一放在捡到碎片④之后（StoryProgress.AddFragment），避免同一件事恭喜两次。
        Debug.Log("[整理谜题] 四件物品已归位，小房间门已打开。");
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
