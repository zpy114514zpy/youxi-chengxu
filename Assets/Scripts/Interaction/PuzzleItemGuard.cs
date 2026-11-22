using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 关卡一「四个玩偶归位」的兜底保险，运行时装配，不改场景文件。
///
/// ① 玩偶被丢在地上滚进家具里、或者穿到地板下面时，自动放回开局位置——
///    否则四个玩偶凑不齐，谜题会永久卡死（玩家看到的现象就是“柜子里那只玩偶不见了”）。
/// ② 开局的柜子里的玩偶如果跟柜门板/柜侧板穿插（半截埋在柜板里，看起来就像“玩偶没出现”），
///    自动沿柜门法线把它往柜子里面挪一点，并保证它完整落在柜门的横向范围内。
/// ③ 玩偶的碰撞体按真实渲染包围盒收紧，避免隔着关着的柜门就把玩偶“吸”走。
/// </summary>
[DisallowMultipleComponent]
public sealed class PuzzleItemGuard : MonoBehaviour
{
    sealed class Entry
    {
        public StoryPlacement placement;
        public PickupItem item;
        public Vector3 home;
        public Quaternion homeRotation;
    }

    readonly List<Entry> entries = new List<Entry>();
    PlayerInventory inventory;
    float nextScan;

    /// <summary>只在有关卡一玩偶谜题时启动。</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (!FindObjectOfType<BedroomToyPuzzle>()) return;
        var host = new GameObject("玩偶归位兜底");
        host.AddComponent<PuzzleItemGuard>();
    }

    // 等一帧再采集：DrawerSetup 之类在 AfterSceneLoad 里补的组件这时候已经就绪。
    void Start() { Collect(); }

    void Collect()
    {
        entries.Clear();
        var pickups = FindObjectsOfType<PickupItem>();
        foreach (var placement in FindObjectsOfType<StoryPlacement>())
        {
            if (!placement || string.IsNullOrEmpty(placement.requiredItemId)) continue;
            PickupItem found = null;
            foreach (var item in pickups)
                if (item && item.storyId == placement.requiredItemId) { found = item; break; }
            if (!found)
            {
                Debug.LogWarning("[玩偶兜底] 场景里找不到 " + placement.requiredItemId + " 对应的玩偶。", placement);
                continue;
            }

            TightenCollider(found.transform);
            KeepOutOfPanels(found.transform);
            // 玩偶是“摆好等着被捡”的静物：刚体必须冻住。
            // 之前它们是普通刚体，开柜门时门扇一扫就会把玩偶顶飞到柜子深处/柜缝里，
            // 玩家再打开柜子就“看不见”了 —— 而且每次开门的时机不同，所以是“有时候”出问题。
            Freeze(found.transform);

            entries.Add(new Entry
            {
                placement = placement,
                item = found,
                home = found.transform.position,
                homeRotation = found.transform.rotation
            });
        }
        if (entries.Count > 0)
            Debug.Log("[玩偶兜底] 已看护 " + entries.Count + " 个玩偶。");
    }

    void Update()
    {
        if (Time.unscaledTime < nextScan || entries.Count == 0) return;
        nextScan = Time.unscaledTime + 1f;

        if (!inventory) inventory = FindObjectOfType<PlayerInventory>();
        if (!inventory) return;      // 拿不到背包就不敢乱复原，免得把玩家手上的玩偶复制一份出来

        foreach (var entry in entries)
        {
            if (!entry.item || !entry.placement || entry.placement.filled) continue;
            if (!entry.item.gameObject) continue;
            if (inventory.HasItem(entry.item.storyId)) continue;   // 正在背包里，正常

            var t = entry.item.transform;
            bool hidden = !entry.item.gameObject.activeInHierarchy;
            bool fell = t.position.y < entry.home.y - 2.5f;
            bool far = (t.position - entry.home).sqrMagnitude > 400f;           // 被丢到 20m 开外

            if (hidden) Restore(entry, "被藏起来了");
            else if (fell) Restore(entry, "掉到地板下面了");
            else if (far) Restore(entry, "被丢得太远了");
        }
    }

    void Restore(Entry entry, string why)
    {
        var t = entry.item.transform;
        t.SetPositionAndRotation(entry.home, entry.homeRotation);

        var rb = entry.item.GetComponent<Rigidbody>();
        if (rb)
        {
            rb.velocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.useGravity = false;
            rb.isKinematic = true;
        }
        foreach (var c in entry.item.GetComponentsInChildren<Collider>(true)) c.enabled = true;
        foreach (var r in entry.item.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
        entry.item.gameObject.SetActive(true);

        Debug.Log("[玩偶兜底] " + entry.item.displayName + " " + why + "，已放回原位。", entry.item);
    }

    // ---------- 碰撞体 ----------

    /// <summary>
    /// 把玩偶的刚体冻成静态：位置只由我们自己控制，不会因为柜门扫到、地板抖动、
    /// 或者别的东西碰一下就滚走 / 掉到柜子底下（那样玩家就再也找不到它了）。
    /// 被玩家捡起、丢弃的逻辑走 PlayerInventory，不受影响。
    /// </summary>
    static void Freeze(Transform t)
    {
        foreach (var body in t.GetComponentsInChildren<Rigidbody>(true))
        {
            if (!body) continue;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.useGravity = false;
            body.isKinematic = true;
        }
    }

    /// <summary>碰撞体按渲染包围盒收紧（原来的盒子和模型经常对不上，容易隔着柜门就拾取到）。</summary>
    static void TightenCollider(Transform t)
    {
        var box = t.GetComponent<BoxCollider>();
        var renderer = t.GetComponent<Renderer>();
        if (!box || !renderer) return;

        Bounds b = renderer.bounds;
        Vector3 s = t.lossyScale;
        float sx = Mathf.Max(.0001f, Mathf.Abs(s.x));
        float sy = Mathf.Max(.0001f, Mathf.Abs(s.y));
        float sz = Mathf.Max(.0001f, Mathf.Abs(s.z));

        box.center = t.InverseTransformPoint(b.center);
        box.size = new Vector3(
            Mathf.Max(.04f, b.size.x) / sx,
            Mathf.Max(.04f, b.size.y) / sy,
            Mathf.Max(.04f, b.size.z) / sz);
        box.isTrigger = false;
        box.enabled = true;
    }

    // ---------- 别跟柜板穿插 ----------

    /// <summary>
    /// 玩偶贴着柜门/柜板时，沿着门的法线往柜子里面退，并保证整个包围盒落在柜门的横向范围里。
    /// 这两件事都会让玩偶“半截埋在柜板里”，从某些角度看就是完全看不见。
    /// </summary>
    void KeepOutOfPanels(Transform t)
    {
        var renderers = t.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;
        Bounds doll = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) doll.Encapsulate(renderers[i].bounds);

        // 附近的门板 / 抽屉面板（含 DrawerSetup 运行时补的那些）
        var panels = new List<FurnitureOpenable>();
        Bounds union = new Bounds();
        bool hasUnion = false;
        foreach (var openable in FindObjectsOfType<FurnitureOpenable>())
        {
            if (!openable) continue;
            var panel = openable.PanelBounds();
            if (Vector3.Distance(panel.center, doll.center) > 1.4f) continue;
            panels.Add(openable);
            if (!hasUnion) { union = panel; hasUnion = true; }
            else union.Encapsulate(panel);
        }
        if (panels.Count == 0) return;
        if (!union.Intersects(doll))
        {
            // 没跟柜板穿插，但可能“躲在柜子端板后面”：这种位置开门也永远看不见，
            // 单独处理一下（只做横向平移，不动高度）。
            PullIntoDoorSpan(t, doll, union, panels[0]);
            return;      // 没跟柜板穿插就别动它，别把摆好的玩偶挪偏
        }

        Vector3 outward = panels[0].PickOutwardByClearance();
        Vector3 axis = Vector3.ProjectOnPlane(outward, Vector3.up);
        if (axis.sqrMagnitude < .01f) return;
        axis.Normalize();
        Vector3 original = t.position;

        // ① 往柜内退：让玩偶的包围盒离开面板的内表面，至少留 2cm。
        float panelInner = MaxAlong(union, -axis);   // 面板朝柜内的那一面
        float dollNear = MinAlong(doll, -axis);      // 玩偶朝柜外的那一面
        float inward = panelInner + .02f - dollNear;
        if (inward > .18f) return;                   // 埋得太深，多半不是这只玩偶该待的地方，别硬挪
        bool moved = false;
        if (inward > .001f)
        {
            t.position += -axis * inward;
            moved = true;
        }

        // ② 横向：完整落在柜门的横向范围里（不压到柜子侧板）
        Vector3 lateral = Vector3.Cross(Vector3.up, axis).normalized;
        doll = RecalcBounds(t, doll);
        float lo = MinAlong(union, lateral) + .02f;
        float hi = MaxAlong(union, lateral) - .02f;
        float dlo = MinAlong(doll, lateral);
        float dhi = MaxAlong(doll, lateral);
        float shift = 0f;
        if (dlo < lo) shift = lo - dlo;
        else if (dhi > hi) shift = hi - dhi;
        if (Mathf.Abs(shift) > .001f && Mathf.Abs(shift) <= .25f)
        {
            t.position += lateral * shift;
            moved = true;
        }

        // 挪完还跟柜板穿插，说明方向判错了：直接退回原样，别弄巧成拙。
        if (!moved) return;
        if (union.Intersects(RecalcBounds(t, doll))) { t.position = original; return; }
        Debug.Log("[玩偶兜底] " + t.name + " 和柜门板穿插，已往柜子里面挪开。", t);
    }

    /// <summary>
    /// 玩偶落在“柜子端板（或侧板）和墙之间的窄缝”里 —— 从外面看明明在柜子里，
    /// 但不管开哪扇柜门都看不见它。判据：在门板后方 0.65m 以内、横向却超出这排柜门的范围。
    /// 命中就横向拉回到柜门范围内（只平移，不抬不降），让玩家开门就能看到。
    /// </summary>
    void PullIntoDoorSpan(Transform t, Bounds doll, Bounds union, FurnitureOpenable reference)
    {
        Vector3 outward = reference.PickOutwardByClearance();
        outward.y = 0f;
        if (outward.sqrMagnitude < .01f) return;
        outward.Normalize();

        // 玩偶在门板后面（柜内）多少 —— 只有“在柜内且不太深”才处理
        float behind = Vector3.Dot(union.center - doll.center, outward);
        if (behind < -.02f || behind > .65f) return;

        Vector3 lateral = Vector3.Cross(Vector3.up, outward).normalized;
        float lo = MinAlong(union, lateral) + .08f;
        float hi = MaxAlong(union, lateral) - .08f;
        float dlo = MinAlong(doll, lateral);
        float dhi = MaxAlong(doll, lateral);
        if (dlo >= lo - .01f && dhi <= hi + .01f) return;    // 本来就在柜门范围内

        float shift;
        if (dhi < lo) shift = lo - dhi;
        else if (dlo > hi) shift = hi - dlo;
        else return;                                          // 跨在边界上，别硬塞
        if (Mathf.Abs(shift) > .45f) return;

        t.position += lateral * shift;
        Debug.Log("[玩偶兜底] " + t.name + " 躲在柜子端板后面，已横向挪回柜门范围内（"
            + shift.ToString("F3") + "m）。", t);
    }

    static Bounds RecalcBounds(Transform t, Bounds fallback)
    {
        var renderers = t.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return fallback;
        Bounds b = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
        return b;
    }

    static float MaxAlong(Bounds b, Vector3 dir)
    {
        return Vector3.Dot(b.center, dir)
            + .5f * (Mathf.Abs(dir.x) * b.size.x + Mathf.Abs(dir.y) * b.size.y + Mathf.Abs(dir.z) * b.size.z);
    }

    static float MinAlong(Bounds b, Vector3 dir) => -MaxAlong(b, -dir);
}
