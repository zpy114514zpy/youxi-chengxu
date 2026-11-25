using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 谜题二《积木块谜题》：二楼一号房间 1 号门口有一个积木堆，五块积木散落在房间各处。
/// 玩家把积木一块块捡起来搬回积木堆、在堆上按 E 放上去；五块全部归位后，
/// 房间里的柜子会自动弹开，露出第二块信封碎片（Line002），拾取后结束本关。
/// 整个积木堆、提示光点、碰撞体都在运行时生成，不改场景文件。
/// </summary>
[DisallowMultipleComponent]
public sealed class BlockPuzzle : MonoBehaviour
{
    [Header("碎片")]
    public string fragmentName = "信封碎片②";
    [TextArea] public string fragmentText = "长大之后我一定会做个追求自己热爱的、开心的人！";

    [Header("放置")]
    [Tooltip("玩家离积木堆超过这个距离就不允许放置（水平距离，单位米）。")]
    public float placeRadius = 2.4f;

    /// <summary>五块积木在积木堆上的位置（相对积木堆中心，3 块一排、2 块一排）。</summary>
    static readonly Vector3[] SlotOffsets =
    {
        new Vector3(-.28f, 0f, -.18f),
        new Vector3( .00f, 0f, -.19f),
        new Vector3( .28f, 0f, -.18f),
        new Vector3(-.15f, 0f,  .19f),
        new Vector3( .15f, 0f,  .18f)
    };

    const float BaseHeight = .056f;

    readonly List<GameObject> blocks = new List<GameObject>();
    readonly List<GameObject> pads = new List<GameObject>();
    readonly List<Transform> pieces = new List<Transform>();
    GameObject fragment;
    Transform drawer;
    PlayerInventory inventory;
    PlayerController player;
    int placed;
    bool solved;
    bool revealing;
    bool fragmentShown;
    Font labelFont;

    public bool Solved => solved;
    public int Placed => placed;
    public int Total => blocks.Count;
    public bool Complete => blocks.Count > 0 && placed >= blocks.Count;

    public string Prompt
    {
        get
        {
            if (solved || Complete) return "";
            if (!inventory) inventory = FindObjectOfType<PlayerInventory>();
            string progress = "积木堆（" + placed + " / " + Mathf.Max(blocks.Count, 1) + "）";
            if (!inventory || !inventory.HasBlock) return progress + " · 先把散落的积木搬过来";
            if (!InPlaceRange()) return progress + " · 离积木堆有点远，走近一点";
            return "E 放置积木（" + placed + " / " + Mathf.Max(blocks.Count, 1) + "）";
        }
    }

    // ---------- 装配 ----------

    /// <summary>由 BlockPuzzleSetup 在进关时调用。</summary>
    public void Build(Vector3 ground, List<Transform> pieces, GameObject fragmentModel)
    {
        transform.position = ground;
        transform.rotation = Quaternion.identity;
        fragment = fragmentModel;

        this.pieces.Clear();
        if (pieces != null) this.pieces.AddRange(pieces);

        BuildBase(pieces);
        AttachPieces(pieces);
        BuildPads();

        // 交互用的碰撞盒：只比底座高一点点，玩家瞄着积木堆就能交互，也不至于变成一堵隐形墙。
        var body = gameObject.AddComponent<BoxCollider>();
        body.center = new Vector3(0f, .07f, 0f);
        body.size = new Vector3(1.00f, .14f, .86f);

        StartCoroutine(StashBedsideBlock());

        Debug.Log("[积木谜题] 积木堆已生成于 " + ground.ToString("F3") + "，可放置积木 " + blocks.Count + " 块。", this);
    }

    /// <summary>
    /// 卧室床边地上那块积木不该散在外面 —— 收进旁边的床头柜抽屉里，玩家得拉开抽屉才能拿到。
    /// DrawerSetup 也在这个时间点装配（顺序不确定），所以等两帧再找已经挂好交互的抽屉。
    /// </summary>
    IEnumerator StashBedsideBlock()
    {
        yield return null;
        yield return null;
        BedsideBlockStash.Stash(pieces);
    }

    void BuildBase(List<Transform> pieces)
    {
        // 底座自己用一块干净的木色材质：直接借积木的贴图会让底座出现一堆杂色块。
        Material wood = WoodMaterial();

        var baseObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
        baseObject.name = "积木堆底座";
        var baseCollider = baseObject.GetComponent<Collider>();
        if (baseCollider) Destroy(baseCollider);
        baseObject.transform.SetParent(transform, false);
        baseObject.transform.localPosition = new Vector3(0f, BaseHeight * .5f, 0f);
        baseObject.transform.localScale = new Vector3(.94f, BaseHeight, .80f);
        if (wood) baseObject.GetComponent<MeshRenderer>().sharedMaterial = wood;

        // 积木堆整体有发光边框：玩家从门口一眼就能看到要往哪儿搬。
        var outline = baseObject.AddComponent<ProximityOutline>();
        outline.revealDistance = 8f;
        outline.outlineWidth = .007f;
        outline.outlineColor = new Color(.25f, .9f, 1f, .95f);
    }

    void BuildPads()
    {
        var material = GlowMaterial(new Color(.40f, .92f, 1f, .55f));
        if (!material) return;                     // 拿不到 shader 就别硬建，宁可没有光点也不要抛异常
        var dot = SoftDot();
        if (dot)
        {
            material.SetTexture("_BaseMap", dot);
            material.mainTexture = dot;
        }
        int count = Mathf.Min(SlotOffsets.Length, Mathf.Max(1, blocks.Count));
        for (int i = 0; i < count; i++)
        {
            var pad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            pad.name = "积木位置";
            var padCollider = pad.GetComponent<Collider>();
            if (padCollider) Destroy(padCollider);
            pad.transform.SetParent(transform, false);
            pad.transform.localPosition = new Vector3(SlotOffsets[i].x, BaseHeight + .002f, SlotOffsets[i].z);
            pad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            pad.transform.localScale = Vector3.one * .17f;
            pad.GetComponent<MeshRenderer>().sharedMaterial = material;
            pads.Add(pad);
        }
    }

    /// <summary>木色底座材质（运行时生成，不改工程资源）。房间偏暗，木色压一点自发光，免得看成一块黑板。</summary>
    static Material WoodMaterial()
    {
        var shader = RuntimeShaders.Lit;
        if (!shader) return null;
        var material = new Material(shader) { name = "积木堆木色" };
        var wood = new Color(.82f, .60f, .36f, 1f);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", wood);
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", .10f);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", .10f);
        if (material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", new Color(.11f, .075f, .045f, 1f));
        }
        material.color = wood;
        return material;
    }

    /// <summary>中间亮、边缘透明的圆形光点贴图，比硬邦邦的方块好看。</summary>
    static Texture2D SoftDot()
    {
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "积木光点",
            wrapMode = TextureWrapMode.Clamp
        };
        var center = new Vector2(size * .5f, size * .5f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + .5f, y + .5f), center) / (size * .5f);
                float alpha = Mathf.Clamp01(1f - distance);
                alpha = alpha * alpha;
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }
        texture.Apply();
        return texture;
    }

    void AttachPieces(List<Transform> pieces)
    {
        for (int i = 0; i < pieces.Count; i++)
        {
            var piece = pieces[i];
            if (!piece) continue;
            var go = piece.gameObject;
            blocks.Add(go);

            // 积木原本没有碰撞体，加一个比网格略大的盒子，玩家好瞄准、也不会被穿模。
            if (!go.GetComponent<Collider>())
            {
                var filter = go.GetComponent<MeshFilter>();
                var box = go.AddComponent<BoxCollider>();
                Vector3 scale = go.transform.lossyScale;
                Vector3 size = filter && filter.sharedMesh ? filter.sharedMesh.bounds.size : Vector3.zero;
                Vector3 pad = new Vector3(
                    .05f / Mathf.Max(.0001f, Mathf.Abs(scale.x)),
                    .05f / Mathf.Max(.0001f, Mathf.Abs(scale.y)),
                    .05f / Mathf.Max(.0001f, Mathf.Abs(scale.z)));
                if (filter && filter.sharedMesh) box.center = filter.sharedMesh.bounds.center;
                box.size = new Vector3(
                    Mathf.Max(size.x + pad.x, pad.x),
                    Mathf.Max(size.y + pad.y, pad.y),
                    Mathf.Max(size.z + pad.z, pad.z));
            }

            var pickup = go.GetComponent<PickupItem>();
            if (!pickup) pickup = go.AddComponent<PickupItem>();
            pickup.displayName = BlockName(piece.name);
            pickup.storyId = "block_" + i;
            pickup.itemType = "积木";
            pickup.description = "散落在房间里的积木块，需要搬回门口的积木堆归位。";

            // 靠近时描边发光，方便玩家在角落里找到它。
            var outline = go.GetComponent<ProximityOutline>();
            if (!outline) outline = go.AddComponent<ProximityOutline>();
            outline.revealDistance = 2.6f;
            outline.outlineWidth = .006f;
            outline.outlineColor = new Color(.25f, .9f, 1f, .95f);
        }
    }

    static string BlockName(string modelName)
    {
        if (modelName.StartsWith("Cone")) return "三角积木";
        if (modelName.StartsWith("Cylinder")) return "圆柱积木";
        if (modelName.StartsWith("Box003")) return "长条积木";
        return "方形积木";
    }

    // ---------- 交互 ----------

    public void Interact(PlayerInventory owner)
    {
        if (solved || Complete) return;
        if (!InPlaceRange())
        {
            StoryProgress.Instance?.EnqueueCenter("提示", "离积木堆太远了，走近一点再放上去。");
            return;
        }
        var item = owner ? owner.TakeBlock() : null;
        if (!item) return;
        GameAudio.PlayPlace();
        StartCoroutine(PlaceRoutine(item.gameObject));
    }

    bool InPlaceRange()
    {
        if (!player) player = FindObjectOfType<PlayerController>();
        if (!player) return false;
        Vector3 a = player.transform.position, b = transform.position;
        if (Mathf.Abs(a.y - b.y) > 1.2f) return false;          // 不在同一层
        a.y = b.y = 0f;
        return Vector3.Distance(a, b) <= placeRadius;
    }

    IEnumerator PlaceRoutine(GameObject block)
    {
        int index = Mathf.Clamp(placed, 0, Mathf.Max(0, SlotOffsets.Length - 1));

        block.SetActive(true);
        foreach (var collider in block.GetComponentsInChildren<Collider>()) collider.enabled = false;
        var outline = block.GetComponent<ProximityOutline>();
        if (outline) outline.enabled = false;

        var renderer = block.GetComponentInChildren<Renderer>();
        Quaternion rotation = block.transform.rotation;
        Vector3 from = block.transform.position;
        // 积木的轴心并不在模型中心（block 模型里每块积木都有很大的局部偏移）。
        // 直接设 transform.position 会让模型落到槽位旁边去，所以先量出“轴心 → 视觉中心”的偏移，
        // 摆放时反着扣掉，保证模型正好落在发光点上、底面贴着积木堆台面。
        Bounds visual = renderer ? renderer.bounds : new Bounds(block.transform.position, Vector3.one * .1f);
        Vector3 offset = visual.center - block.transform.position;
        Vector3 slot = transform.position + SlotOffsets[index];
        float top = transform.position.y + BaseHeight;
        Vector3 to = new Vector3(
            slot.x - offset.x,
            top + visual.extents.y - offset.y + .002f,
            slot.z - offset.z);

        float t = 0f;
        const float duration = .38f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
            if (!block) yield break;
            block.transform.position = Vector3.Lerp(from, to, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * .10f);
            block.transform.rotation = rotation;
            yield return null;
        }
        if (!block) yield break;
        block.transform.position = to;
        block.transform.rotation = rotation;

        if (index < pads.Count && pads[index]) pads[index].SetActive(false);
        placed++;

        if (placed >= blocks.Count) Solve();
    }

    void Solve()
    {
        if (solved) return;
        solved = true;
        StoryProgress.Instance?.RecordClue("五块积木都搬回了门口的积木堆。");
        if (!revealing) StartCoroutine(RevealRoutine());
    }

    // ---------- 柜子弹开、碎片出现 ----------

    IEnumerator RevealRoutine()
    {
        revealing = true;
        var story = StoryProgress.Instance;
        if (story) story.EnqueueCenter("积木归位", "五块积木都回到原处了！");

        // 等这一页提示播完再动柜子，免得玩家只顾着看字、错过柜子弹开。
        float waited = 0f;
        while (waited < 12f && story && story.IsShowingPage) { waited += Time.deltaTime; yield return null; }
        yield return new WaitForSeconds(.2f);

        drawer = FindCabinetDrawer();
        if (drawer)
        {
            var openable = drawer.GetComponent<FurnitureOpenable>();
            if (!openable) openable = drawer.GetComponentInParent<FurnitureOpenable>();
            if (openable)
            {
                if (!player) player = FindObjectOfType<PlayerController>();
                openable.enabled = true;
                openable.Toggle(player ? player.transform.position : drawer.position + drawer.forward);
                yield return new WaitForSeconds(.85f);
                // 抽屉弹开后就不再是交互目标：拆掉开关组件，让它保持打开，准星也不会再提示“打开/关闭抽屉”。
                openable.enabled = false;
                Destroy(openable);
            }
        }
        else
        {
            Debug.LogWarning("[积木谜题] 没找到装碎片的柜子，碎片直接出现在积木堆旁。", this);
        }

        RevealFragment();
    }

    void RevealFragment()
    {
        if (!fragment)
        {
            Debug.LogWarning("[积木谜题] 没有找到信封碎片 Line002。", this);
            return;
        }

        Vector3 position;
        if (drawer)
        {
            Bounds bounds = BoundsOf(drawer);
            Vector3 direction = player
                ? Vector3.ProjectOnPlane(player.transform.position - bounds.center, Vector3.up).normalized
                : Vector3.forward;
            if (direction.sqrMagnitude < .01f) direction = Vector3.forward;
            // 碎片躺在抽屉里（抽屉已经整个弹出来，玩家站在柜子前俯视就能看到）：
            // 位置取抽屉中央、稍微往柜子里面挪一点，别贴着前沿。
            float inner = Mathf.Abs(direction.x) * bounds.extents.x + Mathf.Abs(direction.z) * bounds.extents.z;
            position = bounds.center - direction * (inner * .28f);
            position.y = DrawerFloor(bounds, drawer) + .012f;
            // 挂到抽屉上：以后抽屉再动，碎片也跟着走，不会悬在半空。
            fragment.transform.SetParent(drawer, true);
        }
        else
        {
            position = transform.position + Vector3.up * .12f;
        }

        fragment.transform.rotation = Quaternion.Euler(0f, fragment.transform.eulerAngles.y, 0f);
        fragment.transform.position = position;
        fragment.SetActive(true);
        SetupFragmentPickup();
        EnsureFragmentGlow();
        fragmentShown = true;

        StoryProgress.Instance?.RecordClue("柜子自动弹开了，里面压着一张信封碎片。");
        Debug.Log("[积木谜题] 柜子已弹开，" + fragmentName + "出现在 " + position.ToString("F3"), this);
    }

    /// <summary>
    /// 碎片②只套一圈黄色描边，不再挂点光。
    /// 纸片很小，点光一放在抽屉里就会把整个抽屉内壁烧成一片黄白，纸上的字反而看不见；
    /// 描边本身已经足够把玩家的注意力拉过去。
    /// </summary>
    void EnsureFragmentGlow()
    {
        if (!fragment) return;

        var outline = fragment.GetComponent<ProximityOutline>();
        if (!outline) outline = fragment.AddComponent<ProximityOutline>();
        // 必须用 Apply：描边材质是在 OnEnable 里按默认（青色）建好的，直接改字段不生效。
        outline.Apply(new Color(1f, .80f, .24f, .85f), .016f, 6f);
    }

    /// <summary>碎片上方跟一条“E 拾取”的浮字，配合准星提示一起告诉玩家怎么拿。</summary>
    void OnGUI()
    {
        if (!fragmentShown || !fragment || !fragment.activeSelf) return;
        var story = StoryProgress.Instance;
        if (story && story.IsShowingPage) return;      // 剧情面板正在播时不要叠字
        var cam = Camera.main;
        if (!cam) return;
        if (!player) player = FindObjectOfType<PlayerController>();
        if (player && (player.transform.position - fragment.transform.position).sqrMagnitude > 36f) return;

        Vector3 screen = cam.WorldToScreenPoint(fragment.transform.position + Vector3.up * .16f);
        if (screen.z <= 0f) return;
        if (!labelFont) labelFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 20);
        var style = new GUIStyle(GUI.skin.box) { font = labelFont, fontSize = 15, alignment = TextAnchor.MiddleCenter };
        GUI.Box(new Rect(screen.x - 112f, Screen.height - screen.y - 40f, 224f, 30f), "E 拾取 " + fragmentName, style);
    }

    void OnDestroy()
    {
        if (labelFont) Destroy(labelFont);
    }

    /// <summary>量出抽屉内侧底面高度（照射线，只认抽屉自己的碰撞体）。</summary>
    static float DrawerFloor(Bounds bounds, Transform drawer)
    {
        float floor = bounds.min.y + .05f;
        Vector3 origin = new Vector3(bounds.center.x, bounds.max.y + .25f, bounds.center.z);
        foreach (var hit in Physics.RaycastAll(origin, Vector3.down, bounds.size.y + .8f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (!hit.collider.transform.IsChildOf(drawer)) continue;
            if (hit.point.y > bounds.center.y) continue;      // 抽屉沿口的顶面不算底面
            floor = Mathf.Max(floor, hit.point.y + .012f);
        }
        return floor;
    }

    Transform FindCabinetDrawer()
    {
        if (!fragment) return null;
        Transform best = null;
        float bestDistance = 3f;
        foreach (var openable in FindObjectsOfType<FurnitureOpenable>())
        {
            if (!openable || !openable.slide) continue;
            float distance = Vector3.Distance(openable.transform.position, fragment.transform.position);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            best = openable.transform;
        }
        return best;
    }

    void SetupFragmentPickup()
    {
        var envelope = fragment.GetComponent<EnvelopeFragment>();
        if (!envelope) envelope = fragment.AddComponent<EnvelopeFragment>();
        envelope.fragmentName = fragmentName;
        envelope.text = fragmentText;

        var pickup = fragment.GetComponent<PickupItem>();
        if (!pickup) pickup = fragment.AddComponent<PickupItem>();
        pickup.displayName = fragmentName;
        pickup.storyId = "letter_fragment_2";
        pickup.itemType = "信封碎片";
        pickup.description = fragmentText;

        // 柜子里偏暗，纸片开双面 + 一点自发光，玩家一眼就能看到。
        var renderer = fragment.GetComponentInChildren<Renderer>();
        if (renderer)
        {
            var material = renderer.material;
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", new Color(.16f, .15f, .13f, 1f));
            }
        }

        if (!fragment.GetComponent<Collider>())
        {
            Vector3 scale = fragment.transform.lossyScale;
            Bounds bounds = renderer ? renderer.bounds : new Bounds(fragment.transform.position, Vector3.one * .1f);
            var box = fragment.AddComponent<BoxCollider>();
            box.center = fragment.transform.InverseTransformPoint(bounds.center);
            box.size = new Vector3(
                Mathf.Max(.18f, bounds.size.x) / Mathf.Max(.0001f, Mathf.Abs(scale.x)),
                Mathf.Max(.12f, bounds.size.y) / Mathf.Max(.0001f, Mathf.Abs(scale.y)),
                Mathf.Max(.18f, bounds.size.z) / Mathf.Max(.0001f, Mathf.Abs(scale.z)));
        }
    }

    // ---------- 小工具 ----------

    static float FloorAt(Vector3 p)
    {
        if (Physics.Raycast(new Vector3(p.x, p.y + .45f, p.z), Vector3.down, out var hit, 1.6f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return hit.point.y + .012f;
        return p.y - .3f;
    }

    static Bounds BoundsOf(Transform target)
    {
        var renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(target.position, Vector3.one * .2f);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    /// <summary>加法混合的自发光材质，用来画“这里要放一块积木”的光点。</summary>
    static Material GlowMaterial(Color color)
    {
        var shader = RuntimeShaders.Unlit;
        if (!shader) return null;
        var material = new Material(shader);
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        material.SetFloat("_ZWrite", 0f);
        material.SetFloat("_Cull", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_EMISSION");
        material.renderQueue = 3200;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", color);
        material.color = color;
        return material;
    }
}
