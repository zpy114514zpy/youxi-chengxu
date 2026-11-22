using UnityEngine;

/// <summary>
/// 抽屉 / 柜格里小物件（钥匙这类）的“藏起来 → 拉开才露出 + 提示”组件。
///
/// 为什么不挂常亮的灯：柜子是个半封闭的盒子，物件上只要有一盏常亮的点光，
/// 光就会从板缝里漏出来、把整件家具的正面照得泛光 —— 玩家在房间另一头
/// 就能看见柜子在发亮，等于提前剧透“这里面有东西”。
/// 所以这盏小灯**只在抽屉拉开的这段时间亮**（关着就彻底关掉，不漏光）。
///
/// 位置怎么定（这是前几轮反复出错的地方，写清楚）：
///   · 物件在场景里原本摆在抽屉盒的**深处**（离抽屉前沿十几厘米）。
///     抽屉拉出来时它跟着一起走，但始终比前沿靠里十几厘米 ——
///     玩家站在柜子前面，视线永远先被抽屉前脸板挡住，所以“看不见”。
///   · 因此拉开后把它沿**抽屉自己滑动的方向**（FurnitureOpenable.OpeningDirection，
///     这个方向就是面板实际移动的方向，不用再猜）挪到抽屉前脸内侧 3.5cm：
///     仍然在抽屉盒里面（不会悬空），但就在开口处，一拉开从上面就看得见。
///   · 关抽屉时它跟着缩回柜体（反正那时是隐藏的），绝不留在外面。
/// 物件被玩家拿走 / 丢到地上之后（Release），就永久恢复显示。
/// </summary>
[DisallowMultipleComponent]
public sealed class DrawerItemHint : MonoBehaviour
{
    [Header("描边")]
    public Color outlineColor = new Color(.36f, .88f, 1f, .95f);
    public float outlineWidth = .012f;
    public float revealDistance = 3f;

    [Header("微光（只在拉开时亮）")]
    public Color glowColor = new Color(.50f, .82f, 1f);
    public float glowIntensity = .35f;
    public float glowRange = .40f;

    [Header("抽屉")]
    [Tooltip("抽屉拉开多少（0~1）就把物件显示出来。")]
    [Range(0f, .8f)] public float openThreshold = .40f;
    [Tooltip("自动找抽屉面板的最大距离（米）。")]
    public float searchRadius = .7f;
    [Tooltip("物件摆在抽屉前脸内侧多少米（越小越靠外，但必须留在抽屉盒里）。")]
    public float mouthInset = .035f;

    ProximityOutline outline;
    Light glow;

    FurnitureOpenable drawer;
    Renderer[] ownRenderers;
    Collider[] ownColliders;
    Transform parentBeforeAttach;
    bool reparented;

    Vector3 itemBase;              // 物件在场景里的原始位置（抽屉关着时就该在这）
    Vector3 panelBase;             // 记录时抽屉面板“关着”的位置，用来算位移
    Vector3 mouthOffset;           // 相对抽屉根节点的“抽屉口”偏移
    bool mouthLocked;

    bool shown;
    bool released;
    int tries;
    float nextTry;
    bool logged;

    /// <summary>
    /// 装配。传了面板就直接依附；不传则运行时自己去找最近的那只抽屉
    /// （面板可能是别的脚本晚一帧才挂上的，所以会隔半秒重试）。
    /// </summary>
    public void Setup(FurnitureOpenable source = null)
    {
        ownRenderers = GetComponentsInChildren<Renderer>(true);
        ownColliders = GetComponentsInChildren<Collider>(true);

        outline = GetComponent<ProximityOutline>();
        if (!outline) outline = GetComponentInChildren<ProximityOutline>();
        if (!outline) outline = gameObject.AddComponent<ProximityOutline>();
        // Apply 会同时改已经建好的描边材质；只改字段的话颜色还会是默认的青色。
        outline.Apply(outlineColor, outlineWidth, revealDistance);

        BuildGlow();

        if (source) Attach(source);
        // 找到抽屉之前先老实显示：宁可提前看见，也不能让钥匙凭空消失。
        Show(!drawer);
    }

    /// <summary>一盏只有拉开抽屉时才亮的小灯，让暗柜子里的小东西有个亮点。</summary>
    void BuildGlow()
    {
        if (glow) return;
        var host = new GameObject("微光");
        host.transform.SetParent(transform, false);
        glow = host.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = glowColor;
        glow.intensity = glowIntensity;
        glow.range = glowRange;
        glow.shadows = LightShadows.None;
        glow.renderMode = LightRenderMode.ForcePixel;
        glow.enabled = false;          // 关抽屉时不亮：不会从板缝里漏光
    }

    /// <summary>认下这只抽屉：只记录基准位置，物件本身一个坐标都不改。</summary>
    void Attach(FurnitureOpenable source)
    {
        if (!source || drawer) return;
        drawer = source;
        if (!drawer.enabled) drawer.enabled = true;      // 万一被当成“整件家具”禁用了

        // 面板“关着时”的位置：万一装配时抽屉已经拉开了一点，要先把这段行程扣掉，
        // 否则基准位置会算在拉开后的位置上，关抽屉时物件会被反向推进柜体里。
        panelBase = drawer.transform.position - drawer.OpeningDirection * (drawer.travel * drawer.OpenAmount);
        Vector3 panelDelta = drawer.transform.position - panelBase;
        // 物件此刻相对“抽屉关着时”的位置。抽屉没开时 panelDelta 是零，等于原位。
        itemBase = transform.position - (drawer.slide ? panelDelta : Vector3.zero);

        if (!drawer.slide)
        {
            // 平开门是绕铰链转的，平移跟不准：直接挂到门板下面（保持世界位置），
            // 让它跟着门一起转，露出来时也仍然落在柜格里。
            parentBeforeAttach = transform.parent;
            transform.SetParent(drawer.transform, true);
            reparented = true;
        }

        Show(false);
        Debug.Log("[抽屉小物] " + name + " 已跟到抽屉 " + drawer.name
            + "（面板 " + drawer.PanelBounds().size.ToString("F2") + "），拉开这只抽屉才会显示。", this);
    }

    void Update()
    {
        // 还没找到该依附的抽屉：每半秒试一次。
        if (!released && !drawer && tries < 60 && Time.unscaledTime >= nextTry)
        {
            nextTry = Time.unscaledTime + .5f;
            tries++;
            var found = FindNearbyPanel();
            if (found) Attach(found);
            else if (tries >= 60)
            {
                Debug.LogWarning("[抽屉小物] " + name + " 附近没找到抽屉面板，保持常显。", this);
                Show(true);
            }
        }

        if (released || !drawer) return;

        bool open = drawer.OpenAmount > openThreshold;

        if (drawer.slide)
        {
            // 抽屉一拉开就把“抽屉口”的偏移量算出来（用抽屉自己的滑动方向，不靠猜），
            // 之后每帧只做“抽屉根节点位移 + 固定偏移”，所以物件永远钉在抽屉前脸内侧，
            // 既不会留在柜体深处，也不会飘到柜子外面。
            if (open && !mouthLocked) LockMouth();

            if (mouthLocked) transform.position = drawer.transform.position + mouthOffset;
            else transform.position = itemBase + (drawer.transform.position - panelBase);
        }

        // 显隐只由抽屉开合度决定。
        if (open != shown) Show(open);
    }

    /// <summary>
    /// 算出“抽屉前脸内侧 mouthInset 米”的那个点，换算成相对抽屉根节点的偏移。
    /// 抽屉只平移不旋转，所以这个偏移量在开合过程中保持不变。
    /// </summary>
    void LockMouth()
    {
        Bounds b = drawer.PanelBounds();
        Vector3 outward = drawer.OpeningDirection;      // 抽屉实际滑出的方向
        outward.y = 0f;
        if (outward.sqrMagnitude < .0004f) return;
        outward.Normalize();

        // 面板沿“滑出方向”的半厚
        float half = Mathf.Abs(Vector3.Dot(b.size,
            new Vector3(Mathf.Abs(outward.x), 0f, Mathf.Abs(outward.z)))) * .5f;

        Vector3 mouth = b.center + outward * (half - Mathf.Abs(mouthInset));
        // 高度比原位略抬 2cm（但绝不超过抽屉口上沿）：让它贴着抽屉口，
        // 站在柜子前面不用蹲下也能看到，又不会飘到抽屉外面。
        mouth.y = Mathf.Min(transform.position.y + .02f, b.max.y - .02f);

        mouthOffset = mouth - drawer.transform.position;
        mouthLocked = true;
        Debug.Log("[抽屉小物] " + name + " 已摆到抽屉前脸内侧（" + mouth.ToString("F3")
            + "），滑动方向 " + outward.ToString("F2"), this);
    }

    /// <summary>找离自己最近的那只抽屉面板（排除整件家具的柜体网格）。</summary>
    FurnitureOpenable FindNearbyPanel()
    {
        Vector3 self = Bounds().center;
        FurnitureOpenable best = null;
        float bestScore = float.MaxValue;
        foreach (var openable in FindObjectsOfType<FurnitureOpenable>())
        {
            if (!openable || !openable.enabled) continue;
            Bounds panel = openable.PanelBounds();
            Vector3 size = panel.size;
            if (size.x > 1.2f || size.y > 1.2f || size.z > 1.2f) continue;   // 整件家具，不算面板
            float score = panel.SqrDistance(self);
            if (score > searchRadius * searchRadius) continue;
            if (score >= bestScore) continue;
            bestScore = score;
            best = openable;
        }
        return best;
    }

    /// <summary>被玩家捡走 / 丢到地上：永久恢复显示，并脱离抽屉。</summary>
    public void Release()
    {
        released = true;
        if (reparented && drawer && transform.parent == drawer.transform)
        {
            transform.SetParent(parentBeforeAttach, true);
            reparented = false;
        }
        drawer = null;
        Show(true);
    }

    void Show(bool visible)
    {
        if (shown == visible && ownRenderers != null) return;
        shown = visible;
        if (ownRenderers != null)
            for (int i = 0; i < ownRenderers.Length; i++)
                if (ownRenderers[i]) ownRenderers[i].enabled = visible;
        if (ownColliders != null)
            for (int i = 0; i < ownColliders.Length; i++)
                if (ownColliders[i]) ownColliders[i].enabled = visible;
        if (outline) outline.revealDistance = visible ? revealDistance : .01f;
        // 微光只在露出来时亮：抽屉关着时灯是灭的，不会从板缝漏光。
        if (glow) glow.enabled = visible;

        // 第一次亮起来时打一行日志，方便在 Console 里确认“拉开抽屉 → 提示确实开了”。
        if (visible && !logged && drawer)
        {
            logged = true;
            Debug.Log("[抽屉小物] " + name + " 已露出（" + transform.position.ToString("F3")
                + "），抽屉开合度 " + drawer.OpenAmount.ToString("F2") + "，蓝色描边 + 微光已开启。", this);
        }
    }

    Bounds Bounds()
    {
        var renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(transform.position, Vector3.one * .1f);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            if (renderers[i]) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    void OnDestroy()
    {
        // 兜底：别在场景里留下一件看不见的物件。
        if (!released) Show(true);
    }
}
