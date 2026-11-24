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
/// ④ 玩偶半截穿进柜体/墙里时，找一个真的不穿插的位置最小幅度挪开（一般就落在柜子和墙的缝隙里）。
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
        /// <summary>开局时是不是站在柜子里面（柜内玩偶要一直守着深度，不能被柜门扫到柜背去）。</summary>
        public bool inCabinet;
        /// <summary>柜内方向（世界坐标，水平）。</summary>
        public Vector3 cabinetInward;
        /// <summary>柜门板内表面在 cabinetInward 方向上的坐标。</summary>
        public float cabinetInner;
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

            bool isInCabinet = false;
            Vector3 cabinetInward = Vector3.zero;
            float cabinetInner = 0f;

            TightenCollider(found.transform);
            KeepOutOfPanels(found.transform);
            // 柜内 / 柜外要分开处理：
            //  · 柜外的玩偶半截埋进柜体（穿模），用“找不穿插的位置”那套往外挪；
            //  · 柜内的玩偶绝对不能走那套 —— 柜体的碰撞盒通常是**实心的一整个盒子**，
            //    玩偶站在柜子里必然被判成“三个轴全穿插”，外推只会把它推到柜背/墙里，
            //    玩家一开柜门就再也看不见它（就是“柜子里的玩偶不见了”）。柜内只做深度修正。
            if (IsInsideCabinet(found.transform, out Vector3 cabinetOut, out Bounds cabinetDoors, out Bounds cabinetDoor))
            {
                isInCabinet = true;
                cabinetInward = -cabinetOut;
                cabinetInner = MinAlong(cabinetDoors, cabinetInward);
                SettleInsideCabinet(found.transform, cabinetInward, cabinetInner);
                // 滑门的开法是“整块门板往旁边滑 28cm”——门滑过去的那块正好是它原来的位置。
                // 玩偶要是站在门滑过去的地方，开门反而被门板挡住，看起来就是“柜子里没有东西”。
                // 所以把它横向对到这扇门的正中间：门一滑开，原来那块开口露出来，玩偶就在开口里。
                AlignToCabinetDoor(found.transform, cabinetDoor, cabinetInward);
                Debug.Log("[玩偶兜底] " + found.displayName + " 在柜子内部，按柜内规则摆好："
                    + found.transform.position.ToString("F3") + "（不做穿模外推）", found);
            }
            else
            {
                isInCabinet = false;
                // 先让“美术摆飘了”的玩偶落回家具表面（床上那只粉色玩偶就悬在床面上方 24cm），
                // 落稳之后它才算“站在家具上”，后面的穿模兜底就不会再把它往上顶。
                // 返回 true = 这只玩偶悬得太高、量到的只是地板，属于美术摆位，任何位移都不做。
                if (!DropToSupport(found.transform))
                    SettleOutOfFurniture(found.transform);
            }
            // 玩偶是“摆好等着被捡”的静物：刚体必须冻住。
            // 之前它们是普通刚体，开柜门时门扇一扫就会把玩偶顶飞到柜子深处/柜缝里，
            // 玩家再打开柜子就“看不见”了 —— 而且每次开门的时机不同，所以是“有时候”出问题。
            Freeze(found.transform);

            entries.Add(new Entry
            {
                placement = placement,
                item = found,
                home = found.transform.position,
                homeRotation = found.transform.rotation,
                inCabinet = isInCabinet,
                cabinetInward = cabinetInward,
                cabinetInner = cabinetInner
            });
        }
        if (entries.Count > 0)
        {
            Debug.Log("[玩偶兜底] 已看护 " + entries.Count + " 个玩偶。");
            foreach (var entry in entries)
                if (entry.item)
                    Debug.Log("[玩偶兜底] " + entry.item.displayName + " 最终位置 "
                        + entry.home.ToString("F3") + (entry.inCabinet ? "（柜内）" : "（柜外）"));
        }
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
            // 最后一道保险：柜内的玩偶只要跑偏（被柜门扫到柜背外、卡进柜子深处），
            // 每秒确认一次深度，自动拉回柜门内侧 —— 玩家开门一定能看见它。
            else if (entry.inCabinet)
                SettleInsideCabinet(t, entry.cabinetInward, entry.cabinetInner);
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

    // ---------- 柜子内部 ----------

    /// <summary>
    /// 判断玩偶是不是“站在柜子里面”（而不是柜子旁边的地上）。
    /// 判据：附近那组柜门面板的并集后方 3~85cm、横向落在门宽范围内（放宽 35cm，柜子比门宽）、
    /// 高度落在这一层柜门的上下 55cm 之内。
    /// </summary>
    static bool IsInsideCabinet(Transform t, out Vector3 outward, out Bounds doors, out Bounds nearest)
    {
        outward = Vector3.zero;
        Bounds doll = BoundsOf(t);
        doors = new Bounds(doll.center, Vector3.zero);
        nearest = doors;

        FurnitureOpenable any = null;
        bool has = false;
        float bestDistance = float.MaxValue;
        foreach (var openable in FindObjectsOfType<FurnitureOpenable>())
        {
            if (!openable) continue;
            var panel = openable.PanelBounds();
            float distance = Vector3.Distance(panel.center, doll.center);
            if (distance > 1.5f) continue;
            if (!has) { doors = panel; has = true; any = openable; }
            else doors.Encapsulate(panel);
            if (distance < bestDistance) { bestDistance = distance; nearest = panel; }
        }
        if (!has || !any) return false;

        Vector3 outDir = any.PickOutwardByClearance();
        outDir.y = 0f;
        if (outDir.sqrMagnitude < .01f) return false;
        outDir.Normalize();
        outward = outDir;

        float behind = Vector3.Dot(doors.center - doll.center, outDir);   // >0 = 在门板后方（柜内）
        if (behind < .03f || behind > .85f) return false;

        Vector3 lateral = Vector3.Cross(Vector3.up, outDir).normalized;
        float halfWidth = .5f * (Mathf.Abs(lateral.x) * doors.size.x + Mathf.Abs(lateral.z) * doors.size.z);
        float along = Vector3.Dot(doll.center - doors.center, lateral);
        if (Mathf.Abs(along) > halfWidth + .35f) return false;

        // 高度放宽到 ±1.2m：柜子常常是“上层一组门、下层开放格”，
        // 玩偶摆在下面那格时，跟门板中心能差半米多。
        if (doll.center.y < doors.min.y - 1.2f || doll.center.y > doors.max.y + 1.2f) return false;
        return true;
    }

    /// <summary>
    /// 柜内的玩偶只沿柜门法线做“深度”修正：离门板内侧 10cm 左右最理想
    /// （开门时不被门板挡住，也不会埋进柜子最深处被端板遮住）。
    /// 横向和高度一律不动 —— 场景里摆的位置本来就是对的。
    /// </summary>
    static void SettleInsideCabinet(Transform t, Vector3 inward, float panelInner)
    {
        Bounds doll = BoundsOf(t);

        float dollFront = MinAlong(doll, inward);     // 玩偶朝柜门的那一面
        float gap = dollFront - panelInner;           // >0 表示玩偶整个在门板内侧

        const float want = .10f;
        // 带死区：6cm ~ 55cm 都属于“正常待在柜子里”，不去碰它，免得每秒都被微调抖动。
        if (gap < .06f)
        {
            // 离门板太近（门板一开一合就会顶到它，视觉上像“开一半被切掉”）：往柜里退。
            // 上限放到 45cm：万一被外推逻辑推到柜背外了，这一下也够拉回柜内。
            float push = Mathf.Min(want - gap, .45f);
            t.position += inward * push;
            Physics.SyncTransforms();
            Debug.Log("[玩偶兜底] " + t.name + " 在柜内离门板太近，往柜里退了 "
                + push.ToString("F3") + "m。", t);
        }
        else if (gap > .55f)
        {
            // 埋得太深（柜子端板 / 隔板会挡住视线）：往柜门方向拉回来一点。
            float pull = Mathf.Min(gap - .45f, .20f);
            t.position += -inward * pull;
            Physics.SyncTransforms();
            Debug.Log("[玩偶兜底] " + t.name + " 在柜内埋得太深，往柜门方向拉了 "
                + pull.ToString("F3") + "m。", t);
        }
    }

    /// <summary>
    /// 把柜内玩偶横向对到“它最近的那扇柜门”的正中间。
    ///
    /// 这些柜门是运行时由 DrawerSetup 挂的滑门（slide=true、travel 0.28），开门的动作是
    /// 整块门板往旁边平移 28cm。**门滑过去的那块地方，正好是它刚才站着的位置** ——
    /// 玩偶要是站在那儿，玩家把门拉开，门板反而盖在玩偶前面，看起来就是“这一列里什么都没有”。
    /// 对到门中间以后，门一滑开，露出来的开口正好在玩偶身上。
    /// </summary>
    static void AlignToCabinetDoor(Transform t, Bounds door, Vector3 inward)
    {
        Vector3 lateral = Vector3.Cross(Vector3.up, -inward).normalized;
        Bounds doll = BoundsOf(t);
        float shift = Vector3.Dot(door.center - doll.center, lateral);
        if (Mathf.Abs(shift) < .04f || Mathf.Abs(shift) > .45f) return;   // 已经对上了 / 差太远（多半不是同一个柜子）

        Vector3 half = doll.extents * .97f;
        if (BlockingAt(t, doll.center + lateral * shift, half, out _))
        {
            Debug.LogWarning("[玩偶兜底] " + t.name + " 想横向对到柜门正中，但目标位置有柜板挡着，保持原位。", t);
            return;
        }

        Vector3 original = t.position;
        t.position = original + lateral * shift;
        Physics.SyncTransforms();
        Debug.Log("[玩偶兜底] " + t.name + " 在柜内没对准柜门，横向挪了 "
            + shift.ToString("F3") + "m 到门正中。", t);
    }

    // ---------- 别穿模到柜体里 ----------
    /// <summary>
    /// 玩偶半截穿进柜板/墙里时（玩家看到的就是“玩偶穿模进柜子了”），
    /// 用物理重叠检测找一块真的不跟任何东西相交的位置，最小幅度挪过去。
    /// 判据是“三个轴上的穿插深度都超过 2cm”，所以贴着地板、贴着柜面站立不算穿插，不会乱挪。
    /// 方向支持**竖直向上**：玩偶下半截埋进地板时（截图里只剩一小截露在地面上），
    /// 只往水平方向挪是永远挪不出去的，必须先把它抬起来。
    /// </summary>
    static void SettleOutOfFurniture(Transform t)
    {
        Physics.SyncTransforms();          // KeepOutOfPanels 刚挪过位置，先把物理世界同步过来再量包围盒
        // 用渲染包围盒量（不用碰撞盒）：判断的是“眼睛看到的穿模”，而有些玩偶的碰撞盒并不准。
        Bounds b = RecalcBounds(t, new Bounds(t.position, Vector3.zero));
        if (b.size.sqrMagnitude < 1e-6f) return;

        Vector3 origin = b.center;
        Vector3 half = b.extents * .97f;

        if (!BlockingAt(t, origin, half, out Vector3 away)) return;

        Vector3 original = t.position;

        // 已经是“摆在家具上站着”的玩偶（床垫、柜板、地板就在脚下 10cm 内）：
        // 它跟家具相交是**美术摆的位置**（模型碰撞盒普遍比可见网格大一圈），
        // 往上顶只会让它飘在半空 —— 之前那只床上的粉色玩偶就是这么飞起来的。
        // 这种玩偶只做水平微调，绝不做竖直位移。
        bool standing = HasSupport(t, b);

        // 候选方向：先试“背离挡住它的那个物体”（可能是往上，地板顶住时就是往上），
        // 再试正上方，最后按 45° 铺满一圈水平方向。
        var dirs = new List<Vector3>();
        if (away.sqrMagnitude > .01f)
        {
            away.Normalize();
            if (!standing || Mathf.Abs(away.y) < .20f) dirs.Add(away);
        }
        if (!standing) dirs.Add(Vector3.up);
        Vector3 main = Vector3.ProjectOnPlane(away, Vector3.up);
        if (main.sqrMagnitude > .01f)
        {
            main.Normalize();
            dirs.Add(main);
            dirs.Add(Quaternion.Euler(0f, 30f, 0f) * main);
            dirs.Add(Quaternion.Euler(0f, -30f, 0f) * main);
        }
        for (int i = 0; i < 8; i++)
        {
            Vector3 d = Quaternion.Euler(0f, i * 45f, 0f) * Vector3.forward;
            bool dup = false;
            for (int k = 0; k < dirs.Count; k++)
                if (Vector3.Dot(dirs[k], d) > .97f) { dup = true; break; }
            if (!dup) dirs.Add(d);
        }

        foreach (var dir in dirs)
        {
            // 站着的玩偶不允许竖直位移：它的“相交”是站在家具里，抬起来就是悬空。
            if (standing && Mathf.Abs(dir.y) > .05f) continue;
            // 竖直方向最多抬 6cm：这一档只够把“脚底刚好陷进地板两三厘米”的玩偶拔出来。
            // 需要抬得更高的，是玩偶被家具那整块粗碰撞盒包住了（床、柜子），
            // 抬高只会让它浮在半空 —— 之前床上那只粉色玩偶就是这样飞起来的。宁可不动。
            float maxStep = Mathf.Abs(dir.y) > .05f ? .06f : .35f;
            // 步长 1cm 起、1cm 递增：原来 3cm 起步经常会跨过“刚好不穿”的那个位置。
            for (float step = .01f; step <= maxStep; step += .01f)
            {
                Vector3 candidate = origin + dir * step;
                if (BlockingAt(t, candidate, half, out _)) continue;

                Vector3 delta = candidate - origin;
                t.position = original + delta;
                Physics.SyncTransforms();
                // 站着的玩偶：横挪之后脚下必须还有支撑，否则就是从床/柜板上滑到空中 —— 宁可不动。
                if (standing && !HasSupport(t, BoundsOf(t)))
                {
                    t.position = original;
                    Physics.SyncTransforms();
                    continue;
                }
                Debug.Log("[玩偶兜底] " + t.name + " 半截穿进柜体/墙，已挪到 " + t.position.ToString("F3")
                    + "（方向 " + dir.ToString("F2") + "，挪了 " + delta.magnitude.ToString("F3") + "m）。", t);
                return;
            }
        }
        // 站着的玩偶：横向也没有空位（它就是摆在床/柜板上），保持美术摆的原位。
        // 宁可让它跟粗包围盒轻微相交，也不能抬高（会悬空）、不能缩小（会变形）。
        if (standing)
        {
            Debug.Log("[玩偶兜底] " + t.name + " 站在家具上，横向让不开，保持原位不动。", t);
            return;
        }
        // 实在找不到空位（玩偶太大、缝太窄）：最后把模型整体缩一点再试。
        // 这种玩偶是纯摆件，缩小 18% 肉眼几乎看不出，但能立刻从缝里退出来。
        Debug.LogWarning("[玩偶兜底] " + t.name + " 卡在柜体/墙里，没找到不穿插的位置，试着缩小一点。", t);
        Vector3 scale = t.localScale;
        t.localScale = scale * .82f;
        Physics.SyncTransforms();
        Bounds smaller = BoundsOf(t);
        if (!BlockingAt(t, smaller.center, smaller.extents * .97f, out _))
        {
            Debug.Log("[玩偶兜底] " + t.name + " 缩小到 82% 后不再穿模。", t);
            return;
        }
        t.localScale = scale;
    }

    /// <summary>
    /// 玩偶是不是“站在家具上”：在它脚底正下方 10cm ~ 2cm 之间放一块薄片，看有没有别的东西重叠。
    /// 这一类玩偶跟家具相交是美术摆位造成的（模型碰撞盒比可见网格大一圈），不能靠“抬起来”解决。
    /// 用 OverlapBox 而不是向下射线：玩偶已经被粗碰撞盒“包住”时，射线起点在盒子内部，
    /// MeshCollider 从内部打是打不到的（Unity 默认不打背面），会误判成“悬空”。
    /// </summary>
    static bool HasSupport(Transform t, Bounds b)
    {
        Vector3 center = new Vector3(b.center.x, b.min.y - .04f, b.center.z);
        Vector3 half = new Vector3(b.extents.x * .70f, .06f, b.extents.z * .70f);
        var hits = Physics.OverlapBox(center, half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        foreach (var hit in hits)
        {
            if (!hit || hit.isTrigger) continue;
            if (hit.transform == t || hit.transform.IsChildOf(t)) continue;
            return true;
        }
        // 碰撞盒量不到时（家具常常是一整块粗 MeshCollider，脚底那块反而没有碰撞），
        // 再按**渲染面**量一次：脚下 12cm 内有看得见的东西，同样算“站在上面”。
        return FindSurface(t, b, out float surface, out _) && b.min.y - surface < .12f;
    }

    /// <summary>
    /// 把悬空的玩偶放下，让它的脚底贴到下面那个东西的表面上。
    ///
    /// 做法：从包围盒**头顶上方**往下打 5 条线（起点必须在身体外面，否则从内部打不穿整块 MeshCollider），
    /// 取脚底以下最高的那个面，把包围盒底部贴上去（留 2mm 不接触）。
    /// 只要间隙 &lt; 6cm 就不动 —— 本来摆好的玩偶不受影响。
    ///
    /// 但悬空超过 35cm 时**绝对不能放**：床、柜的碰撞体里往往没有被子、软垫这些细节，
    /// 射线会一路穿到地板上，把玩偶塞进床底下（玩家就完全看不见这只玩偶了）。
    /// 这一点已经实测踩过：`toy_1 悬在 SM_Floor_1-2-Attic_1200 上方 0.511m，已落到 3.302`
    /// 就是掉进床底的那次。这种玩偶属于美术摆位，保持原样即可。
    /// </summary>
    /// <returns>true = 判定为“美术摆位”（悬空过高、量到的只是地板），不要对它做任何位移。</returns>
    static bool DropToSupport(Transform t)
    {
        Physics.SyncTransforms();
        Bounds b = BoundsOf(t);
        if (b.size.sqrMagnitude < 1e-6f) return false;

        if (!FindSurface(t, b, out float surface, out string who))
        {
            Debug.Log("[玩偶落地] " + t.name + " 脚底 " + b.min.y.ToString("F3")
                + "m，下方没量到比脚底低的物体，保持原位。", t);
            return false;
        }

        float gap = b.min.y - surface;
        // 悬空超过 35cm：量到的多半是地板（床/柜的碰撞体里没有被子、枕头），
        // 这时候下放只会把玩偶塞到床底下。判定为美术摆位，完全不动。
        if (gap > .35f)
        {
            Debug.Log("[玩偶落地] " + t.name + " 脚底 " + b.min.y.ToString("F3") + "m，下方最近的面是 "
                + who + "（" + surface.ToString("F3") + "m），悬空 " + gap.ToString("F3")
                + "m 过高，判定为美术摆位，保持原位。", t);
            return true;
        }
        if (gap < .06f)
        {
            Debug.Log("[玩偶落地] " + t.name + " 脚底 " + b.min.y.ToString("F3") + "m，下面是 "
                + who + "（面 " + surface.ToString("F3") + "m），只差 " + gap.ToString("F3") + "m，不动。", t);
            return false;
        }

        t.position += Vector3.down * (gap - .002f);
        Physics.SyncTransforms();
        Debug.Log("[玩偶落地] " + t.name + " 悬在 " + who + " 上方 " + gap.ToString("F3")
            + "m，已落到 (" + t.position.ToString("F3") + ")。", t);
        return false;
    }

    /// <summary>
    /// 量“脚底正下方最近的那个能落脚的面”，返回它的世界高度。
    ///
    /// 关键是**射线必须从身体外面往下打**（起点在包围盒上方 75cm）：
    /// 这套房屋里床、柜子都是一整块 MeshCollider，MeshCollider 默认不打背面，
    /// 从内部起点的射线会直接穿过去（这就是之前“怎么量都量不到床面”的原因）。
    /// 起点抬到头顶以上后，射线先穿过玩偶自己（被过滤掉），再命中被子/床垫的真实表面。
    /// 命中点高于脚底的一律不算（那是玩偶自己的碰撞盒顶面），所以能稳定取到脚下的面。
    /// </summary>
    static bool FindSurface(Transform t, Bounds b, out float surface, out string who)
    {
        surface = float.MinValue;
        who = "";

        float from = b.max.y + .75f;
        float reach = from - b.min.y + .60f;
        Vector3[] probes =
        {
            b.center,
            b.center + new Vector3(b.extents.x * .55f, 0f, b.extents.z * .55f),
            b.center + new Vector3(-b.extents.x * .55f, 0f, b.extents.z * .55f),
            b.center + new Vector3(b.extents.x * .55f, 0f, -b.extents.z * .55f),
            b.center + new Vector3(-b.extents.x * .55f, 0f, -b.extents.z * .55f),
        };
        foreach (var probe in probes)
        {
            Vector3 origin = new Vector3(probe.x, from, probe.z);
            float best = float.MaxValue;
            float hitY = float.MinValue;
            string nm = "";
            foreach (var hit in Physics.RaycastAll(origin, Vector3.down, reach, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!hit.collider || hit.collider.isTrigger) continue;
                if (hit.collider.transform == t || hit.collider.transform.IsChildOf(t)) continue;
                if (hit.point.y > b.min.y - .005f) continue;   // 脚底以上的命中不是落脚面（多是自己的身体盒）
                if (hit.distance >= best) continue;
                best = hit.distance;
                hitY = hit.point.y;
                nm = hit.collider.name;
            }
            if (hitY <= surface) continue;
            surface = hitY;
            who = nm;
        }
        return surface != float.MinValue;
    }

    /// <summary>center/half 这个盒子有没有真埋进别的物体；away 返回背离对方的方向（可能带竖直分量）。</summary>
    static bool BlockingAt(Transform self, Vector3 center, Vector3 half, out Vector3 away)
    {
        away = Vector3.zero;
        Bounds mine = new Bounds(center, half * 2f);
        bool blocked = false;
        float worst = 0f;

        var hits = Physics.OverlapBox(center, half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        foreach (var hit in hits)
        {
            if (!hit || hit.isTrigger) continue;
            if (hit.transform == self || hit.transform.IsChildOf(self)) continue;

            Bounds other = hit.bounds;
            Vector3 overlap = Vector3.Min(mine.max, other.max) - Vector3.Max(mine.min, other.min);
            float depth = Mathf.Min(overlap.x, Mathf.Min(overlap.y, overlap.z));
            if (depth < .015f) continue;     // 只是贴着（地板、柜面），不算穿模

            blocked = true;
            if (depth <= worst) continue;
            Vector3 dir = center - other.center;     // 三维：地板顶住时这个方向就是朝上
            if (dir.sqrMagnitude < .0001f) continue;
            worst = depth;
            away = dir.normalized;
        }
        return blocked;
    }

    /// <summary>玩偶当前所有渲染器合成的世界包围盒。</summary>
    static Bounds BoundsOf(Transform t)
    {
        return RecalcBounds(t, new Bounds(t.position, Vector3.one * .02f));
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
