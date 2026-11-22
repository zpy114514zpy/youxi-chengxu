using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 关卡五·时钟信件谜题的运行时装配（不改场景文件）：
/// ① 客厅圆桌上的纸条 → 读到「写下日记的那一刻，就是正确的时间」；
/// ② 方桌上几本凌乱的日记本 → 翻开看到「凌晨三点半」；
/// ③ 墙上挂钟可按 E 拨针，拨到 03:30 → 方桌抽屉自己弹开；
/// ④ 抽屉里有信封碎片⑤和一张纸条，纸条指引去二楼放置；
/// ⑤ 拾取碎片后提示去二楼“获取钥匙的地点”，放下之后进入 Level 2。
/// </summary>
public sealed class LivingRoomClockPuzzle : MonoBehaviour
{
    public static LivingRoomClockPuzzle Instance { get; private set; }

    public ClockDial dial;
    FurnitureOpenable drawer;
    Transform clockFace;
    readonly List<Transform> notes = new List<Transform>();

    Transform desk;
    Vector3 drawerSpot;        // 碎片最终摆放点（抽屉内）
    Transform fragment;
    Transform depositMarker;
    bool fragmentTaken;
    bool revealed;

    const string FragmentName = "信封碎片⑤";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (SceneManager.GetActiveScene().name != "Level 1") return;   // 只装在第一关
        var go = new GameObject("客厅时钟谜题");
        go.AddComponent<LivingRoomClockPuzzle>();
    }

    void Awake() { Instance = this; }

    IEnumerator Start()
    {
        yield return null;    // 等 DrawerSetup 先把家具面板挂完

        if (!BuildClock()) { Debug.LogWarning("[时钟谜题] 没找到挂钟，跳过。"); yield break; }
        BuildRoundTableNote();
        BuildNotebooks();
        FindDrawer();

        Debug.Log("[时钟谜题] 装配完成：纸条 " + notes.Count + " 份，抽屉=" + (drawer ? drawer.name : "无"));
    }

    // ---------------- 挂钟 ----------------

    bool BuildClock()
    {
        var clock = FindExact("clock");
        if (!clock) return false;

        clockFace = FindChildContains(clock, "clock_face");
        var hour = FindChildContains(clock, "clock_armHour");
        var minute = FindChildContains(clock, "clock_armMinute");
        if (!clockFace || !hour) return false;

        // 钟面本身是“贴墙贴纸”式的通透材质（数字环以外透出墙纸），这是美术资源本来的画法，
        // 不动它。只给钟面补一个薄碰撞盒，射线才打得到；指针不挡。
        var col = clockFace.GetComponent<BoxCollider>();
        if (!col) col = clockFace.gameObject.AddComponent<BoxCollider>();
        Bounds b = BoundsOf(clockFace);
        Vector3 s = clockFace.lossyScale;
        col.center = clockFace.InverseTransformPoint(b.center);
        col.size = new Vector3(
            Mathf.Max(.06f, b.size.x) / Mathf.Max(.0001f, Mathf.Abs(s.x)),
            Mathf.Max(.06f, b.size.y) / Mathf.Max(.0001f, Mathf.Abs(s.y)),
            Mathf.Max(.03f, b.size.z) / Mathf.Max(.0001f, Mathf.Abs(s.z)));

        var outline = clock.GetComponent<ProximityOutline>();
        if (!outline) outline = clock.gameObject.AddComponent<ProximityOutline>();
        outline.outlineColor = new Color(.95f, .82f, .45f, 1f);
        outline.outlineWidth = .006f;      // 钟只有 29cm，描边粗了会糊成一圈
        outline.revealDistance = 3.4f;

        dial = clock.GetComponent<ClockDial>();
        if (!dial) dial = clock.gameObject.AddComponent<ClockDial>();
        dial.hourHand = hour;
        dial.minuteHand = minute;
        dial.onSolved = OnClockSolved;
        return true;
    }

    void OnClockSolved()
    {
        if (revealed) return;
        revealed = true;
        StoryProgress.Instance?.EnqueueCenter("咔哒", "指针停在三点半。桌子那边「咔哒」响了一声。");
        // 谜题一解开就把二楼放置点亮起来：玩家从抽屉里拿到碎片后，抬头就知道该往哪走
        BuildDepositMarker();
        StartCoroutine(RevealFragment());
    }

    // ---------------- 圆桌纸条 ----------------

    void BuildRoundTableNote()
    {
        var table = FindExact("SM_Table_A1");
        if (!table) { Debug.LogWarning("[时钟谜题] 没找到客厅圆桌。"); return; }
        Bounds tb = BoundsOf(table);

        foreach (var t in AllTransforms())
        {
            if (!t.name.StartsWith("SM_NewsPaper_A1")) continue;
            Bounds b = BoundsOf(t);
            if (b.size.sqrMagnitude < .0001f) continue;
            Vector2 flat = new Vector2(b.center.x - tb.center.x, b.center.z - tb.center.z);
            if (flat.magnitude > .9f) continue;                        // 只认桌面上那几张
            if (b.center.y < tb.max.y - .12f || b.center.y > tb.max.y + .40f) continue;

            var clue = t.GetComponent<ReadableClue>();
            if (!clue) clue = t.gameObject.AddComponent<ReadableClue>();
            clue.title = "纸条";
            clue.description = "写下日记的那一刻，就是正确的时间。";
            clue.guideAfterRead = "线索我记得日记里面有。";
            clue.emissive = false;                                     // 只留描边，别叠自发光
            clue.glowRange = 2.2f;

            AddOutline(t, new Color(1f, .82f, .35f, 1f));
            notes.Add(t);
        }
    }

    // ---------------- 方桌日记本 ----------------

    void BuildNotebooks()
    {
        desk = FindExact("SM_Table_A3");
        if (!desk) { Debug.LogWarning("[时钟谜题] 没找到客厅方桌。"); return; }
        Bounds db = BoundsOf(desk);

        int count = 0;
        foreach (var t in AllTransforms())
        {
            string n = t.name.ToLowerInvariant();
            if (!n.Contains("book") && !n.Contains("notebook")) continue;
            var r = t.GetComponent<Renderer>();
            if (!r) continue;
            Bounds b = r.bounds;
            if (b.size.sqrMagnitude < .0004f) continue;

            Vector2 flat = new Vector2(b.center.x - db.center.x, b.center.z - db.center.z);
            if (flat.magnitude > 1.1f) continue;
            if (b.center.y < db.max.y - .15f || b.center.y > db.max.y + .45f) continue;   // 只认摊在桌面上的

            var clue = t.GetComponent<ReadableClue>();
            if (!clue) clue = t.gameObject.AddComponent<ReadableClue>();
            clue.title = "日记本";
            clue.description = "纸页有点卷，字迹潦草。\n\n最后一页只写了一行：「凌晨三点半。」";
            clue.guideAfterRead = "凌晨三点半——去看看客厅墙上的挂钟。";
            clue.emissive = false;
            clue.glowRange = 2.2f;

            AddOutline(t, new Color(.6f, .9f, 1f, 1f));
            notes.Add(t);
            count++;
        }
        Debug.Log("[时钟谜题] 桌面上找到日记本 " + count + " 本");
    }

    // ---------------- 抽屉 ----------------

    void FindDrawer()
    {
        var drawerTransform = FindExact("SM_Table_A3_Cab1") ?? FindExact("SM_Table_A3_Cab2");
        if (!drawerTransform) { Debug.LogWarning("[时钟谜题] 没找到方桌抽屉。"); return; }

        drawer = drawerTransform.GetComponent<FurnitureOpenable>();
        if (!drawer) drawer = drawerTransform.GetComponentInParent<FurnitureOpenable>();

        if (!drawer)
        {
            // DrawerSetup 没管到这块面板时自己补一个：只往外直拉。
            drawer = drawerTransform.gameObject.AddComponent<FurnitureOpenable>();
            Bounds b = BoundsOf(drawerTransform);
            drawer.slide = true;
            drawer.travel = Mathf.Clamp(Mathf.Min(b.size.x, b.size.z) * .82f, .18f, .34f);
            drawer.speed = .9f;
        }
    }

    IEnumerator RevealFragment()
    {
        if (drawer)
        {
            drawer.speed = .9f;
            drawer.Open();
            float guard = 0f;
            while (guard < 4f && drawer.OpenAmount < .995f) { guard += Time.deltaTime; yield return null; }
        }
        yield return new WaitForSecondsRealtime(.2f);

        Bounds db = drawer ? BoundsOf(drawer.transform) : BoundsOf(desk);
        drawerSpot = new Vector3(db.center.x, db.min.y + .012f, db.center.z);

        SpawnNote(drawerSpot + new Vector3(0f, 0f, -.075f));
        SpawnFragment(drawerSpot + new Vector3(0f, .002f, .06f));

        StoryProgress.Instance?.RecordClue("方桌抽屉自己弹开了，里面压着信封碎片⑤和一张纸条。");
        Debug.Log("[时钟谜题] 抽屉已弹出，碎片放在 " + drawerSpot.ToString("F3"));
    }

    void SpawnNote(Vector3 position)
    {
        var t = CreatePaper("抽屉里的纸条", new Vector3(.15f, .004f, .10f), position, 18f);
        var box = t.gameObject.AddComponent<BoxCollider>();
        FitCollider(box, t, new Vector3(.15f, .02f, .10f));

        var clue = t.gameObject.AddComponent<ReadableClue>();
        clue.title = "纸条";
        clue.description = "指引：去二楼左侧角落，把信封碎片放在那里。";
        clue.emissive = false;
        clue.glowRange = 1.8f;
        AddOutline(t, new Color(1f, .82f, .35f, 1f));
    }

    void SpawnFragment(Vector3 position)
    {
        var t = CreatePaper(FragmentName, new Vector3(.19f, .004f, .13f), position, -12f);
        fragment = t;

        var pick = t.gameObject.AddComponent<PickupItem>();
        pick.displayName = FragmentName;
        pick.storyId = "letter_fragment_5";
        pick.itemType = "信封碎片";
        pick.description = "抽屉里那张压在最底下的纸片，边角写着半个字。";

        var frag = t.gameObject.AddComponent<EnvelopeFragment>();
        frag.fragmentName = FragmentName;
        frag.text = "抽屉最底下压着的那张纸，像是从信封上撕下来的一角。";

        var box = t.gameObject.AddComponent<BoxCollider>();
        FitCollider(box, t, new Vector3(.19f, .02f, .13f));

        var outline = t.gameObject.AddComponent<ProximityOutline>();
        outline.outlineColor = new Color(1f, .78f, .16f, 1f);
        outline.outlineWidth = .012f;
        outline.revealDistance = 3.2f;

        var lampHost = new GameObject("碎片指引光");
        lampHost.transform.SetParent(t, false);
        lampHost.transform.localPosition = Vector3.up * .22f;
        var lamp = lampHost.AddComponent<Light>();
        lamp.type = LightType.Point;
        lamp.color = new Color(1f, .88f, .42f);
        lamp.range = .62f;
        lamp.intensity = .08f;
        lamp.shadows = LightShadows.None;
    }

    // ---------------- 拾取后 → 二楼放置点 ----------------

    void Update()
    {
        if (fragmentTaken || !fragment) return;
        // 碎片被捡走时 PlayerInventory 会把它 SetActive(false)
        if (!fragment.gameObject.activeSelf)
        {
            fragmentTaken = true;
            BuildDepositMarker();
            var story = StoryProgress.Instance;
            story?.EnqueueCenter("信封碎片", "请将获取的所有信封碎片放至二楼获取钥匙的地点。");
        }
    }

    void BuildDepositMarker()
    {
        if (depositMarker) return;

        var key = FindExact("key(1)");
        Vector3 at = key ? key.position : new Vector3(-23.93f, 3.46f, -98.87f);

        // 放置点必须真的落在能放东西的平面上：从上方往下打一条射线，取它打在柜顶 / 地面上的高度。
        float top = at.y;
        if (Physics.Raycast(at + Vector3.up * 1.6f, Vector3.down, out var hit, 3.4f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            top = hit.point.y + .015f;

        // 蓝色发光圆盘：原来是块报纸材质的薄板，和地板一个颜色，玩家根本看不见。
        var spot = new Vector3(at.x, top, at.z);
        var pad = CreatePad("信封碎片放置点", spot);

        var box = pad.gameObject.AddComponent<BoxCollider>();
        FitCollider(box, pad, new Vector3(.42f, .30f, .42f), new Vector3(0f, .15f, 0f));

        var outline = pad.gameObject.AddComponent<ProximityOutline>();
        outline.outlineColor = new Color(.35f, .62f, 1f, 1f);
        outline.outlineWidth = .016f;
        outline.revealDistance = 7f;

        // 三盏蓝灯从下往上摞，形成一根看得见的光柱；FragmentDeposit 会让它们一起呼吸。
        float[] heights = { .08f, .42f, .92f };
        float[] ranges = { .85f, 1.15f, 1.55f };
        float[] powers = { .55f, .40f, .28f };
        for (int i = 0; i < heights.Length; i++)
        {
            var host = new GameObject("放置点蓝光" + i);
            host.transform.SetParent(pad, false);
            // 用世界坐标：pad 被压成 0.006 厚的圆盘，localPosition 会跟着缩得没法用
            host.transform.position = spot + Vector3.up * heights[i];
            var lamp = host.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = new Color(.34f, .60f, 1f);
            lamp.range = ranges[i];
            lamp.intensity = powers[i];
            lamp.shadows = LightShadows.None;
        }

        var dep = pad.gameObject.AddComponent<FragmentDeposit>();
        dep.CacheGlows();
        depositMarker = pad;
        Debug.Log("[时钟谜题] 放置点已生成：" + pad.position.ToString("F3"));
    }

    static Material padMat;
    static Transform CreatePad(string name, Vector3 position)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        var builtin = go.GetComponent<Collider>();
        if (builtin) Destroy(builtin);

        go.transform.localScale = new Vector3(.44f, .006f, .44f);
        go.transform.position = position + Vector3.up * .008f;

        if (!padMat)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (!sh) sh = Shader.Find("Unlit/Color");
            padMat = new Material(sh);
            var blue = new Color(.22f, .48f, 1f, 1f);
            if (padMat.HasProperty("_BaseColor")) padMat.SetColor("_BaseColor", blue);
            padMat.color = blue;
        }
        var r = go.GetComponent<Renderer>();
        if (r) r.sharedMaterial = padMat;
        return go.transform;
    }

    // ---------------- 小工具 ----------------

    static Transform CreatePaper(string name, Vector3 size, Vector3 position, float yaw)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        var builtin = go.GetComponent<Collider>();
        if (builtin) Destroy(builtin);                 // 自带的碰撞盒尺寸不对，按需重加

        go.transform.localScale = new Vector3(size.x, size.y, size.z);
        go.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));

        var r = go.GetComponent<Renderer>();
        if (r) r.sharedMaterial = PaperMaterial();
        return go.transform;
    }

    static Material paperMat;
    static Material PaperMaterial()
    {
        if (paperMat) return paperMat;
        // 借场景里现成的报纸材质，避免运行时新建 Standard 材质在 URP 下变紫
        foreach (var t in AllTransforms())
        {
            if (!t.name.StartsWith("SM_NewsPaper_A1")) continue;
            var r = t.GetComponent<Renderer>();
            if (r && r.sharedMaterial) { paperMat = r.sharedMaterial; return paperMat; }
        }
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        paperMat = new Material(shader) { color = new Color(.92f, .9f, .86f) };
        return paperMat;
    }

    /// <summary>按“世界尺寸”设碰撞盒：纸片被 localScale 压得很扁，size 必须先除掉缩放。</summary>
    static void FitCollider(BoxCollider box, Transform t, Vector3 worldSize, Vector3 worldCenterOffset = default)
    {
        Vector3 s = t.lossyScale;
        float sx = Mathf.Max(.0001f, Mathf.Abs(s.x));
        float sy = Mathf.Max(.0001f, Mathf.Abs(s.y));
        float sz = Mathf.Max(.0001f, Mathf.Abs(s.z));
        box.size = new Vector3(worldSize.x / sx, worldSize.y / sy, worldSize.z / sz);
        box.center = new Vector3(worldCenterOffset.x / sx, worldCenterOffset.y / sy, worldCenterOffset.z / sz);
    }

    static void AddOutline(Transform t, Color color)
    {
        var outline = t.GetComponent<ProximityOutline>();
        if (!outline) outline = t.gameObject.AddComponent<ProximityOutline>();
        outline.outlineColor = color;
        outline.outlineWidth = .008f;
        outline.revealDistance = 2.6f;
    }

    static Bounds BoundsOf(Transform t)
    {
        var rs = t.GetComponentsInChildren<Renderer>(true);
        bool has = false;
        Bounds b = new Bounds(t.position, Vector3.zero);
        foreach (var r in rs)
        {
            if (!r) continue;
            if (!has) { b = r.bounds; has = true; }
            else b.Encapsulate(r.bounds);
        }
        return b;
    }

    static IEnumerable<Transform> AllTransforms()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid()) yield break;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                yield return t;
    }

    static Transform FindExact(string name)
    {
        foreach (var t in AllTransforms()) if (t.name == name) return t;
        return null;
    }

    static Transform FindChildContains(Transform root, string part)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name.Contains(part)) return t;
        return null;
    }
}
