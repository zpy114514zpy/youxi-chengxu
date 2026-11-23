using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 谜题二 · 归位残破玩具。
/// 流程：在二楼二号房间捡齐 4 个破损玩偶 → 去三号房洗手台按 E 一个个洗干净
///      → 摆回二楼一号房间的柜面 / 凳面 / 床 → 再去拿吉他，放到一号房一号小房间的沙发边。
/// 吉他一开始是“不可交互”的：4 个玩偶没全部摆好之前，它连拾取提示都不会出现。
/// </summary>
public sealed class Level2Toys : MonoBehaviour
{
    // 场景里的玩偶节点名（按摆放点一一对应）。
    static readonly string[] ToyNodes = { "模型", "模型 (1)", "猪猪厨师3d模型", "模型 (2)" };
    static readonly string[] ToyNames = { "旧玩偶 · 一", "旧玩偶 · 二", "猪猪厨师", "旧玩偶 · 三" };
    static readonly string[] ToyIds = { "l2_toy_1", "l2_toy_2", "l2_toy_3", "l2_toy_4" };
    const float FloorY = 3.20f;      // 二楼地板高度

    Level2Ui Ui => Level2Director.Instance ? Level2Director.Instance.Ui : null;

    readonly List<Transform> toys = new List<Transform>();
    readonly HashSet<string> washed = new HashSet<string>();
    readonly List<Level2ToySpot> spots = new List<Level2ToySpot>();
    Level2WashBasin basin;
    Transform guitar;
    Level2GuitarSpot guitarSpot;

    bool unlocked, foundAll, placedAll, guitarTaken, guitarPlaced, leftRoomNotified;
    public bool Unlocked => unlocked;

    public void Setup()
    {
        BuildToys();
        BuildBasin();
        BuildSpots();
        BuildGuitar();
    }

    public void Unlock()
    {
        unlocked = true;
        Debug.Log("[谜题二] 已解锁");
    }

    // ---------------- 玩偶 ----------------

    void BuildToys()
    {
        for (int i = 0; i < ToyNodes.Length; i++)
        {
            var t = L2.Find(ToyNodes[i]);
            if (!t) { Debug.LogWarning("[谜题二] 找不到玩偶 " + ToyNodes[i]); continue; }
            if (t.parent && t.parent.name.StartsWith("道具_")) { toys.Add(t.parent); continue; }

            var host = L2.WrapPickup(t, ToyNames[i], ToyIds[i], "玩偶", .2f, .12f,
                "一只落满灰的旧玩偶，边角已经磨破了。");
            if (host) toys.Add(host);
        }
        Debug.Log($"[谜题二] 可拾取玩偶 {toys.Count} 个");
    }

    // ---------------- 洗手台 ----------------

    void BuildBasin()
    {
        var shelf = L2.Find("BP_BathShelf_B1_0_SM_BathShelf_B1_0_68");
        if (!shelf) { Debug.LogWarning("[谜题二] 找不到洗手台"); return; }

        var b = L2.BoundsOf(shelf);
        var host = new GameObject("洗手台·交互");
        host.transform.position = b.center + Vector3.up * .05f;
        var box = host.AddComponent<BoxCollider>();
        box.size = new Vector3(Mathf.Max(.55f, b.size.x), Mathf.Max(1.0f, b.size.y), Mathf.Max(.55f, b.size.z));

        basin = host.AddComponent<Level2WashBasin>();
        basin.host = this;
        Debug.Log($"[谜题二] 洗手台交互点 {L2.Fmt(host.transform.position)}");
    }

    // ---------------- 摆放点 ----------------

    void BuildSpots()
    {
        // ① 五斗柜柜面：两个玩偶分左右放
        var drawer = L2.Find("BP_Drawer_A2_SM_Drawer_A1_0_129");
        if (drawer)
        {
            var b = L2.BoundsOf(drawer);
            AddSpot("柜面·左", ToyIds[0], ToyNames[0], TopSpot(b, -1f), 90f);
            AddSpot("柜面·右", ToyIds[1], ToyNames[1], TopSpot(b, 1f), 90f);
        }

        // ② 凳面：猪猪厨师
        var chair = L2.Find("SM_Chair_C2");
        if (chair)
        {
            var b = L2.BoundsOf(chair);
            Vector3 seat = new Vector3(b.center.x, b.min.y + .47f, b.center.z);
            if (Physics.Raycast(new Vector3(seat.x, b.center.y + .55f, seat.z), Vector3.down, out var hit, 1.2f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && hit.point.y > b.min.y + .25f)
                seat.y = hit.point.y + .005f;
            AddSpot("凳面", ToyIds[2], ToyNames[2], seat, 180f);
        }
        else Debug.LogWarning("[谜题二] 找不到凳子 SM_Chair_C2");

        // ③ 床面：最后一个玩偶
        var bed = L2.Find("SM_Bed_A1");
        if (bed)
        {
            var b = L2.BoundsOf(bed);
            Vector3 onBed = new Vector3(b.center.x + .35f, b.min.y + .58f, b.center.z + .25f);
            if (Physics.Raycast(new Vector3(onBed.x, b.max.y + .4f, onBed.z), Vector3.down, out var hit, 1.4f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && hit.point.y > b.min.y + .30f)
                onBed.y = hit.point.y + .005f;
            AddSpot("床面", ToyIds[3], ToyNames[3], onBed, 205f);
        }
        else Debug.LogWarning("[谜题二] 找不到床 SM_Bed_A1");
    }

    /// <summary>在柜顶找一个真的落在台面上的点：台面比包围盒窄的时候，往中间收。</summary>
    Vector3 TopSpot(Bounds b, float dir)
    {
        float x = b.center.x;
        for (float d = .34f; d >= .09f; d -= .04f)
        {
            float z = b.center.z + dir * d;
            // 只在台面这一层往下打 25cm：矮柜、抽屉、电视都不会被误命
            if (Physics.Raycast(new Vector3(x, b.max.y + .06f, z), Vector3.down, out var hit, .25f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && Mathf.Abs(hit.point.y - b.max.y) < .06f)
                return new Vector3(x, hit.point.y + .004f, z);
        }
        return new Vector3(x, b.max.y + .004f, b.center.z + dir * .10f);
    }

    void AddSpot(string label, string id, string name, Vector3 position, float yaw)
    {
        var host = new GameObject("摆放点·" + label);
        host.transform.position = position;

        // 交互盒要比玩偶大一圈，玩家不用瞄得那么死。
        // 注意不能用 trigger：玩家的拾取射线是 QueryTriggerInteraction.Ignore，trigger 会被直接跳过。
        var box = host.AddComponent<BoxCollider>();
        box.size = new Vector3(.40f, .40f, .40f);

        var spot = host.AddComponent<Level2ToySpot>();
        spot.host = this;
        spot.requiredId = id;
        spot.displayName = name;
        spot.label = label;
        spot.yaw = yaw;

        // 蓝色提示：捡齐四个玩偶、进了一号房之后才亮，摆好一个灭一个。
        var hintGo = new GameObject("摆放提示");
        hintGo.transform.SetParent(host.transform, false);
        var hint = hintGo.AddComponent<Level2SpotHint>();
        hint.Build(label == "凳面" ? .15f : .17f, .55f);
        spot.hint = hint;

        spots.Add(spot);
    }

    // ---------------- 吉他 ----------------

    void BuildGuitar()
    {
        // 场景里有两个同名的 guitar（父 + 子），取子渲染器多的那个当整体。
        Transform best = null;
        int bestCount = -1;
        foreach (var t in L2.FindAll("guitar"))
        {
            int n = t.GetComponentsInChildren<Renderer>(true).Length;
            if (n > bestCount) { bestCount = n; best = t; }
        }
        if (!best) { Debug.LogWarning("[谜题二] 找不到吉他"); return; }

        guitar = best;
        // 一开始什么都不加：不能拾取、也没有蓝色描边。
        // 玩家把四个玩偶全部摆好之后，这里才挂上 PickupItem 和描边。
        Debug.Log($"[谜题二] 吉他 {L2.Fmt(guitar.position)}（暂不可交互）");
    }

    void UnlockGuitar()
    {
        if (!guitar) return;
        if (guitar.GetComponentInChildren<PickupItem>(true)) return;

        var host = L2.WrapPickup(guitar, "破损的吉他", "l2_guitar", "乐器", .3f, .3f,
            "断了两根弦的旧吉他，琴身上还有小时候贴的贴纸。");
        if (!host) return;
        guitar = host;
        ui_Note("吉他", "角落里的吉他现在可以拿了 ——\n拿上它，放回二楼一号房一号小房间的沙发旁边。");
        Debug.Log("[谜题二] 吉他已解锁拾取");
    }

    void ui_Note(string title, string body) => Ui?.Note(title, body);

    // ---------------- 状态查询 ----------------

    public bool IsWashed(string id) => washed.Contains(id);

    public int WashedCount => washed.Count;

    /// <summary>四个归位点（自检/存档用）。</summary>
    public System.Collections.Generic.IReadOnlyList<Level2ToySpot> Spots => spots;

    public int BagCount(PlayerInventory inv)
    {
        if (!inv) return 0;
        int n = 0;
        foreach (var id in ToyIds) if (inv.HasItem(id)) n++;
        return n;
    }

    public bool IsToy(string id)
    {
        foreach (var t in ToyIds) if (t == id) return true;
        return false;
    }

    // ---------------- 清洗 ----------------

    public bool WashOne(PlayerInventory inv)
    {
        if (!inv) return false;
        foreach (var id in ToyIds)
        {
            if (washed.Contains(id)) continue;
            if (!inv.HasItem(id)) continue;
            washed.Add(id);

            // 洗干净 = 换成第一关那套干净材质（几何是同一份，只是贴图不再发灰发脏）。
            int idx = System.Array.IndexOf(ToyIds, id);
            if (idx >= 0 && idx < toys.Count && toys[idx]) Level2CleanToys.Cleanse(toys[idx]);

            Ui?.Note("清洗玩偶", $"洗掉了一层灰，也变回了干净的样子（{washed.Count} / 4）。");
            if (washed.Count >= ToyIds.Length && !placedAll)
                Ui?.Say("可以放回去了", "四个玩偶都洗干净了。把它们放回二楼一号房间原来的位置吧。");
            return true;
        }
        return false;
    }

    // ---------------- 摆放 ----------------

    public bool CanPlace(Level2ToySpot spot, PlayerInventory inv, out string reason)
    {
        reason = "";
        if (spot.filled) { reason = "这里已经摆好了"; return false; }
        if (!unlocked) { reason = "先把全家福修好"; return false; }
        if (washed.Count < ToyIds.Length) { reason = "得先把四个玩偶都洗干净"; return false; }
        if (!inv || !inv.HasItem(spot.requiredId)) { reason = "手里没有这只玩偶"; return false; }
        return true;
    }

    public void Place(Level2ToySpot spot, PlayerInventory inv)
    {
        var toy = inv.TakeItem(spot.requiredId);
        if (!toy) return;

        toy.transform.SetParent(null, true);
        toy.gameObject.SetActive(true);

        // 有的旧玩偶比柜面还宽，先按摆放点的大小收一收，别让它杵到墙上的电视去。
        toy.transform.rotation = Quaternion.Euler(0f, spot.yaw, 0f);
        var wide = L2.BoundsOf(toy.transform);
        float span = Mathf.Max(wide.size.x, wide.size.z);
        if (span > .38f) toy.transform.localScale *= .38f / span;

        // 真的把玩偶搬到摆放点上：X/Z 对齐，Y 让底部正好贴在柜面 / 凳面 / 床上。
        Settle(toy.transform, spot.transform.position);

        foreach (var c in toy.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        var outline = toy.GetComponentInChildren<ProximityOutline>(true);
        if (outline) outline.enabled = false;
        var pick = toy.GetComponentInChildren<PickupItem>(true);
        if (pick) pick.enabled = false;

        spot.filled = true;
        if (spot.hint) spot.hint.SetVisible(false);
        int done = 0;
        foreach (var s in spots) if (s.filled) done++;
        if (done < spots.Count) Ui?.Note("归位", $"{spot.displayName} 放好了（{done} / {spots.Count}）。");
        else OnAllPlaced();
    }

    /// <summary>
    /// 把摆件的底部放到台面上。
    /// 玩偶刚从背包里 SetActive 回来时，Renderer.bounds 还是上一帧的旧值，
    /// 直接量会把玩偶算高/算低（就是“浮在柜面上”的那种）；所以先量一次，
    /// 等一帧让 Unity 刷新包围盒，再校正一次。
    /// </summary>
    void Settle(Transform toy, Vector3 surface)
    {
        AlignBottom(toy, surface);
        StartCoroutine(SettleNextFrame(toy, surface));
    }

    System.Collections.IEnumerator SettleNextFrame(Transform toy, Vector3 surface)
    {
        yield return null;
        if (toy) AlignBottom(toy, surface);
        Physics.SyncTransforms();
    }

    static void AlignBottom(Transform toy, Vector3 surface)
    {
        var b = ContactBounds(toy);
        if (b.size.y < .001f) return;
        Vector3 p = toy.position;
        p.x = surface.x;
        p.z = surface.z;
        p.y += surface.y - b.min.y;
        toy.position = p;
    }

    /// <summary>
    /// 摆件“真正接触台面”的那一层。
    /// 有的模型里带一片贴地的阴影/底座薄片，把整组渲染器一起算会把玩偶顶到半空，
    /// 所以先按体积找出主体，再把明显是平片的（高度 &lt; 2cm）剔掉。
    /// </summary>
    static Bounds ContactBounds(Transform t)
    {
        Renderer main = null;
        float best = -1f;
        var all = t.GetComponentsInChildren<Renderer>(true);
        foreach (var r in all)
        {
            if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            var s = r.bounds.size;
            float v = s.x * s.y * s.z;
            if (v > best) { best = v; main = r; }
        }
        if (!main) return L2.BoundsOf(t);

        var result = main.bounds;
        foreach (var r in all)
        {
            if (!r || r == main || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            var s = r.bounds.size;
            if (s.y < .02f && s.x * s.z > .02f) continue;      // 贴地阴影片，不参与“脚底”
            result.Encapsulate(r.bounds);
        }
        return result;
    }

    void OnAllPlaced()
    {
        if (placedAll) return;
        placedAll = true;
        Debug.Log("[谜题二] 四个玩偶全部归位");
        Ui?.Say("归位完成", "玩偶放置好了，该去收拾我的吉他了。");
        UnlockGuitar();
    }

    // ---------------- 吉他 ----------------

    public bool CanTakeGuitar(PlayerInventory inv, out string reason)
    {
        reason = "";
        if (!placedAll) { reason = "先把四个玩偶都放回去"; return false; }
        return true;
    }

    public void OnGuitarTaken()
    {
        if (guitarTaken) return;
        guitarTaken = true;
        Ui?.Say("吉他", "我需要把吉他也放回二楼一号房间 ——\n放在一号小房间那张沙发旁边，让它靠着沙发立好。");
        BuildGuitarSpot();
    }

    void BuildGuitarSpot()
    {
        if (guitarSpot) return;

        chair = L2.Find("SM_Chair_B3");
        if (!chair) chair = L2.Find("SM_Chair_B1");
        if (!chair) Debug.LogWarning("[谜题二] 一号小房间里找不到沙发 SM_Chair_B3");

        Vector3 target;
        float yaw;
        Vector3 leanTo;
        FindGuitarBesideChair(chair, out target, out yaw, out leanTo);

        var host = new GameObject("吉他放置点");
        host.transform.position = target;
        var box = host.AddComponent<BoxCollider>();
        // 交互盒只包住吉他下半截，贴着地，不挡人走路。
        box.center = Vector3.up * .34f;
        box.size = new Vector3(.62f, .70f, .62f);

        guitarSpot = host.AddComponent<Level2GuitarSpot>();
        guitarSpot.host = this;
        guitarSpot.yaw = yaw;
        guitarSpot.leanTo = leanTo;
        Debug.Log($"[谜题二] 吉他放置点 {L2.Fmt(target)} 朝向 {yaw:F0}° 靠向 {L2.Fmt(leanTo)}");
    }

    // 一号小房间里那张扶手椅（沙发）。
    Transform chair;

    /// <summary>
    /// 吉他靠在「一号小房间」那张沙发边上。
    /// 这把椅子是贴着墙的（背后只有 0.2m），所以不能简单地绕一圈取第一个空位：
    ///   · 方向按「离门越近越优先」排序，保证从 A6 门进来第一眼就能看见吉他；
    ///   · 脚下必须是二楼地板（打到家具顶面不算）；
    ///   · 用球形扫描量真实间隙（不能用 OverlapCapsule —— 墙和地板是 MeshCollider，
    ///     非凸网格只会按 AABB 判定，会把半个房间都算成挡路）。
    /// </summary>
    void FindGuitarBesideChair(Transform chair, out Vector3 pos, out float yaw, out Vector3 leanTo)
    {
        var b = chair ? L2.BoundsOf(chair)
            : new Bounds(new Vector3(-17.10f, 3.6f, -91.10f), new Vector3(1.0f, 1.0f, 1.0f));
        Vector3 c = b.center;
        c.y = FloorY;

        // 小房间的门：进门能看见的那一侧优先
        var door = L2.Find("BP_Door_Inside_A6");
        Vector3 towardDoor = door ? door.position - c : Vector3.back;
        towardDoor.y = 0f;
        if (towardDoor.sqrMagnitude < .01f) towardDoor = Vector3.back;
        towardDoor.Normalize();

        var dirs = new List<Vector3>
        {
            new Vector3(-.707f, 0f, -.707f), Vector3.back, Vector3.left,
            new Vector3(.707f, 0f, -.707f),
            new Vector3(-.707f, 0f, .707f), Vector3.right,
            new Vector3(.707f, 0f, .707f), Vector3.forward,
        };
        dirs.Sort((x, y) => Vector3.Dot(y, towardDoor).CompareTo(Vector3.Dot(x, towardDoor)));

        foreach (var dir in dirs)
        {
            float edge = Mathf.Abs(dir.x) * b.extents.x + Mathf.Abs(dir.z) * b.extents.z;
            foreach (float gap in new[] { .26f, .33f, .20f })
            {
                Vector3 p = new Vector3(c.x + dir.x * (edge + gap), FloorY, c.z + dir.z * (edge + gap));
                if (!OnFloor(p, out p.y)) continue;
                if (!ClearAround(p, chair)) continue;

                pos = p;
                // 琴面朝着沙发：yaw 让模型正面指向沙发，再叠一个模型自己的偏转。
                Vector3 toChair = -dir;
                yaw = Mathf.Atan2(toChair.x, toChair.z) * Mathf.Rad2Deg - 12f;
                leanTo = toChair;
                Debug.Log($"[谜题二] 沙发边找到位置 {L2.Fmt(p)}（方向 {L2.Fmt(dir)}，离沙发 {gap:F2}m）");
                return;
            }
        }

        // 兜底：沙发正前方 0.4m，朝着门
        pos = new Vector3(c.x + towardDoor.x * (b.extents.x + .40f), FloorY,
                          c.z + towardDoor.z * (b.extents.z + .40f));
        yaw = Mathf.Atan2(-towardDoor.x, -towardDoor.z) * Mathf.Rad2Deg;
        leanTo = -towardDoor;
        Debug.LogWarning("[谜题二] 沙发边没找到空位，退回沙发前方");
    }

    /// <summary>这个点上脚下是不是二楼地板（打到沙发/家具顶面不算）。</summary>
    bool OnFloor(Vector3 p, out float y)
    {
        y = FloorY;
        if (!Physics.Raycast(p + Vector3.up * .55f, Vector3.down, out var hit, 1.0f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return false;
        if (Mathf.Abs(hit.point.y - FloorY) > .07f) return false;
        y = hit.point.y;
        return true;
    }

    /// <summary>
    /// 这个点上立一把琴，四周有没有空。
    /// 用球形扫描（SphereCast 对 MeshCollider 是按真实网格求交的），沙发本身允许挨着 ——
    /// 琴就是要靠在它上面。
    /// </summary>
    bool ClearAround(Vector3 p, Transform chair)
    {
        var dirs = new[]
        {
            Vector3.forward, Vector3.back, Vector3.right, Vector3.left,
            new Vector3(.707f, 0f, .707f), new Vector3(-.707f, 0f, .707f),
            new Vector3(.707f, 0f, -.707f), new Vector3(-.707f, 0f, -.707f),
        };
        foreach (float h in new[] { .35f, .80f })
        {
            Vector3 o = p + Vector3.up * h;
            foreach (var d in dirs)
            {
                if (!Physics.SphereCast(o, .15f, d, out var hit, .30f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                var t = hit.transform;
                if (chair && t && t.IsChildOf(chair)) continue;      // 沙发可以贴
                if (hit.point.y <= p.y + .06f) continue;             // 地毯这种贴地薄片
                return false;
            }
        }
        return true;
    }

    public void PlaceGuitar(PlayerInventory inv)
    {
        var item = inv.TakeItem("l2_guitar");
        if (!item || !guitarSpot) return;
        AttachGuitar(item);
    }

    /// <summary>把吉他搬到沙发上靠好（放置和读档共用这一段）。</summary>
    void AttachGuitar(PickupItem item)
    {
        if (!item || !guitarSpot) return;

        item.transform.SetParent(null, true);
        item.gameObject.SetActive(true);

        Vector3 toChair = guitarSpot.leanTo;
        toChair.y = 0f;
        if (toChair.sqrMagnitude < .001f) toChair = Vector3.forward;
        toChair.Normalize();

        // 模型在场景里本来就是“靠着墙立着”的姿态（琴身在下、琴颈朝上），
        // 所以不要去搜什么“最高的姿态”，只调方位角：
        // 挑一个让琴的薄边朝向沙发的角度 —— 琴背贴沙发、琴面朝外。
        // （这里用 WorldBoundsFast：同一帧里改完旋转，renderer.bounds 还没刷新。）
        Quaternion orig = item.transform.rotation;
        Quaternion upright = orig;
        float bestThin = float.MaxValue;
        float toChairYaw = Mathf.Atan2(toChair.x, toChair.z) * Mathf.Rad2Deg;
        for (int d = 0; d < 360; d += 10)
        {
            item.transform.rotation = Quaternion.AngleAxis(d, Vector3.up) * orig;
            var b = WorldBoundsFast(item.transform);
            float thin = Mathf.Abs(toChair.x) * b.size.x + Mathf.Abs(toChair.z) * b.size.z;
            if (thin < bestThin - .002f) { bestThin = thin; upright = item.transform.rotation; }
        }

        // 再往沙发那边斜 8°，让“靠着”看得出来；真正的接触距离由下面的贴靠步骤补。
        Quaternion lean = Quaternion.AngleAxis(8f, Vector3.Cross(toChair, Vector3.up).normalized);
        item.transform.rotation = lean * upright;
        Debug.Log($"[谜题二] 吉他立姿 方位 {toChairYaw:F0}° 薄边 {bestThin:F2}m");

        Vector3 at = guitarSpot.transform.position;
        item.transform.position = at;
        AlignBottom(item.transform, at);

        // 自己的碰撞体先关掉，免得下面探测的时候打到自己。
        foreach (var c in item.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        Physics.SyncTransforms();

        // 贴靠 + 落地放到下一帧做：这一帧里 renderer.bounds 还是旧值，
        // 当场量包围盒会把琴“贴”到半空去。
        StartCoroutine(ContactSettle(item.transform, toChair));

        var outline = item.GetComponentInChildren<ProximityOutline>(true);
        if (outline) outline.enabled = false;
        var pick = item.GetComponentInChildren<PickupItem>(true);
        if (pick) pick.enabled = false;

        guitarSpot.filled = true;
        OnAllDone();
    }

    /// <summary>
    /// 立刻就能用的世界包围盒：局部包围盒乘变换矩阵自己算。
    /// renderer.bounds 要等一帧才刷新，做“转一圈挑姿态”这种同帧比较时不能用它。
    /// </summary>
    static Bounds WorldBoundsFast(Transform t)
    {
        bool has = false;
        var total = new Bounds();
        foreach (var r in t.GetComponentsInChildren<Renderer>(true))
        {
            if (!r || !r.enabled) continue;
            var lb = r.localBounds;
            var m = r.transform.localToWorldMatrix;
            Vector3 c = m.MultiplyPoint3x4(lb.center);
            Vector3 ex = m.MultiplyVector(new Vector3(lb.extents.x, 0f, 0f));
            Vector3 ey = m.MultiplyVector(new Vector3(0f, lb.extents.y, 0f));
            Vector3 ez = m.MultiplyVector(new Vector3(0f, 0f, lb.extents.z));
            Vector3 e = new Vector3(
                Mathf.Abs(ex.x) + Mathf.Abs(ey.x) + Mathf.Abs(ez.x),
                Mathf.Abs(ex.y) + Mathf.Abs(ey.y) + Mathf.Abs(ez.y),
                Mathf.Abs(ex.z) + Mathf.Abs(ey.z) + Mathf.Abs(ez.z));
            var nb = new Bounds(c, e * 2f);
            if (!has) { total = nb; has = true; } else total.Encapsulate(nb);
        }
        return has ? total : new Bounds(t.position, Vector3.zero);
    }

    Vector3 FloorPoint(Transform t)
    {
        float y = guitarSpot ? guitarSpot.transform.position.y : FloorY;
        return new Vector3(t.position.x, y, t.position.z);
    }

    /// <summary>把琴往沙发那边推到刚刚碰到，再让琴脚落回地板。</summary>
    System.Collections.IEnumerator ContactSettle(Transform t, Vector3 toChair)
    {
        yield return null;                                   // 等包围盒刷新
        if (!t) yield break;
        AlignBottom(t, FloorPoint(t));
        Physics.SyncTransforms();
        yield return null;

        var gb = L2.BoundsOf(t);
        float reach = Mathf.Abs(toChair.x) * gb.extents.x + Mathf.Abs(toChair.z) * gb.extents.z;

        // 单一高度会从扶手和靠背之间的缝里漏过去，所以在四个高度各探一次，取最近的那次。
        float nearest = -1f;
        foreach (float h in new[] { .28f, .48f, .68f, .88f })
        {
            Vector3 o = new Vector3(gb.center.x, FloorY + h, gb.center.z);
            if (!Physics.Raycast(o, toChair, out var hit, 1.10f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
            if (hit.point.y <= FloorY + .05f) continue;      // 打到地板不算
            float gap = hit.distance - reach;
            if (nearest < 0f || gap < nearest) nearest = gap;
        }

        float moved = 0f;
        if (nearest >= 0f)
        {
            moved = Mathf.Clamp(nearest - .012f, -.30f, .45f);
            if (Mathf.Abs(moved) > .004f) t.position += toChair * moved;
        }

        yield return null;
        AlignBottom(t, FloorPoint(t));
        Physics.SyncTransforms();

        var f = L2.BoundsOf(t);
        Debug.Log($"[谜题二] 吉他归位 {L2.Fmt(t.position)} 包围盒 {L2.Fmt(f.size)} 底 y={f.min.y:F3} " +
                  $"往沙发贴了 {moved:F3}m（探到的最近间隙 {(nearest >= 0f ? nearest : -1f):F3}）");
    }

    void OnAllDone()
    {
        if (guitarPlaced) return;
        guitarPlaced = true;
        Debug.Log("[谜题二] 吉他归位，谜题二完成");
        Ui?.Say("内心独白", "玩具已经坏了，就像当年那个满怀期待的小孩，也受过很多伤。\n但我依旧可以把他好好安放。");
        Ui?.Note("新的目标", "去一楼客厅看看吧。");
        if (Level2Director.Instance && Level2Director.Instance.Mirror) Level2Director.Instance.Mirror.Unlock();
    }

    // ---------------- 存档 ----------------

    public string CaptureState()
    {
        var placedIds = new List<string>();
        foreach (var s in spots) if (s && s.filled) placedIds.Add(s.requiredId);
        var washedIds = new List<string>(washed);
        return string.Join("|", new[]
        {
            unlocked ? "1" : "0",
            placedAll ? "1" : "0",
            guitarTaken ? "1" : "0",
            guitarPlaced ? "1" : "0",
            string.Join(",", washedIds),
            string.Join(",", placedIds),
        });
    }

    public void RestoreState(string state)
    {
        if (string.IsNullOrEmpty(state)) return;
        var p = state.Split('|');
        if (p.Length < 6) return;

        unlocked = p[0] == "1";
        guitarTaken = p[2] == "1";
        guitarPlaced = p[3] == "1";

        foreach (var id in p[4].Split(','))
        {
            if (string.IsNullOrEmpty(id) || washed.Contains(id)) continue;
            washed.Add(id);
            int i = System.Array.IndexOf(ToyIds, id);
            if (i >= 0 && i < toys.Count && toys[i]) Level2CleanToys.Cleanse(toys[i]);
        }

        foreach (var id in p[5].Split(','))
        {
            if (string.IsNullOrEmpty(id)) continue;
            foreach (var s in spots)
                if (s && s.requiredId == id && !s.filled) RestoreSpot(s);
        }

        placedAll = p[1] == "1" || AllSpotsFilled();

        var inv = FindObjectOfType<PlayerInventory>();
        if (inv)
        {
            // 存档时吉他如果在背包里，读档也把它放回背包（不然它会留在场景里当装饰）。
            if (guitarTaken && !guitarPlaced && guitar)
            {
                var pick = guitar.GetComponentInChildren<PickupItem>(true);
                if (pick) inv.Take(pick);
            }
            foundAll = foundAll || BagCount(inv) >= ToyIds.Length;
        }
        foundAll = foundAll || washed.Count > 0 || placedAll;

        if (guitarPlaced) { BuildGuitarSpot(); RestoreGuitar(); }

        Debug.Log($"[谜题二] 读档恢复：洗了 {washed.Count} 个，摆了 {PlacedCount()} 个，吉他 {guitarTaken}/{guitarPlaced}");
    }

    int PlacedCount()
    {
        int n = 0;
        foreach (var s in spots) if (s && s.filled) n++;
        return n;
    }

    bool AllSpotsFilled()
    {
        if (spots.Count == 0) return false;
        foreach (var s in spots) if (!s || !s.filled) return false;
        return true;
    }

    void RestoreSpot(Level2ToySpot spot)
    {
        int idx = System.Array.IndexOf(ToyIds, spot.requiredId);
        if (idx < 0 || idx >= toys.Count || !toys[idx]) return;

        var toy = toys[idx];
        toy.SetParent(null, true);
        toy.gameObject.SetActive(true);
        toy.rotation = Quaternion.Euler(0f, spot.yaw, 0f);

        var wide = L2.BoundsOf(toy);
        float span = Mathf.Max(wide.size.x, wide.size.z);
        if (span > .38f) toy.localScale *= .38f / span;

        Settle(toy, spot.transform.position);
        foreach (var c in toy.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        var outline = toy.GetComponentInChildren<ProximityOutline>(true);
        if (outline) outline.enabled = false;
        var pick = toy.GetComponentInChildren<PickupItem>(true);
        if (pick) pick.enabled = false;

        spot.filled = true;
        if (spot.hint) spot.hint.SetVisible(false);
    }

    void RestoreGuitar()
    {
        if (!guitar || !guitarSpot) return;
        var pick = guitar.GetComponentInChildren<PickupItem>(true);
        if (!pick) return;
        AttachGuitar(pick);
    }

    // ---------------- 房间离开检测 ----------------

    void Update()
    {
        var player = Level2Director.Instance ? Level2Director.Instance.Player : null;

        if (unlocked && !leftRoomNotified && player)
        {
            // 二号房间在 z 更小的一侧，穿过 A2 门（z≈-101.7）就算离开了这个房间。
            if (player.position.y > 2.8f && player.position.z > -101.0f && !placedAll)
            {
                leftRoomNotified = true;
                Ui?.Say("谜题二", "请完成谜题二：归位残破玩具。\n\n这个房间里的玩偶和吉他，本来都该在隔壁。");
            }
        }

        if (!foundAll && unlocked)
        {
            var inv = FindObjectOfType<PlayerInventory>();
            if (inv && BagCount(inv) >= ToyIds.Length)
            {
                foundAll = true;
                Ui?.Say("玩偶", "四个玩偶都找到了。我需要去二层的另一个房间去进行清洗。");
            }
        }

        // 吉他什么时候被捡走，这里每帧看一眼（不设“只查一次”的开关，否则错过一次就再也不触发）。
        if (placedAll && !guitarTaken)
        {
            var inv = FindObjectOfType<PlayerInventory>();
            if (inv && inv.HasItem("l2_guitar")) OnGuitarTaken();
        }

        RefreshHints(player);
    }

    /// <summary>
    /// 四个摆放点的蓝色提示什么时候亮：
    /// 玩偶全部拿到手 + 已经进了二楼一号房（门开了，或者人已经站在一号房里）。
    /// 已经摆好的那一处自己灭掉。
    /// </summary>
    void RefreshHints(Transform player)
    {
        bool inRoom1 = player && player.position.y > 2.8f && player.position.z > -95.8f;
        bool doorOpen = Level2Director.Instance && Level2Director.Instance.Doors &&
                        Level2Director.Instance.Doors.IsOpen("A6");
        bool show = foundAll && (doorOpen || inRoom1) && !placedAll;

        foreach (var s in spots)
        {
            if (!s || !s.hint) continue;
            s.hint.SetVisible(show && !s.filled);
        }
    }
}

/// <summary>洗手台：按 E 洗一只还没洗过的玩偶。</summary>
public sealed class Level2WashBasin : L2Interactable
{
    public Level2Toys host;

    public override string Prompt
    {
        get
        {
            if (!host || !host.Unlocked) return "";
            if (host.WashedCount >= 4) return "四个玩偶都洗干净了";
            return "E 清洗玩偶";
        }
    }

    public override void Interact(PlayerInventory inv)
    {
        if (!host) return;
        if (host.WashedCount >= 4) return;
        if (!host.WashOne(inv))
        {
            var ui = Level2Director.Instance ? Level2Director.Instance.Ui : null;
            ui?.Note("洗手台", "手里的玩偶都已经洗过了，还有几只没找到。");
        }
    }
}

/// <summary>一个玩偶的归位点。</summary>
public sealed class Level2ToySpot : L2Interactable
{
    public Level2Toys host;
    public string requiredId, displayName, label;
    public float yaw;
    public bool filled;
    public Level2SpotHint hint;

    public override string Prompt
    {
        get
        {
            if (!host) return "";
            if (filled) return "已经摆好了";
            if (!host.Unlocked) return "";
            if (host.WashedCount < 4) return "E 摆放 " + displayName + "（先洗干净）";
            var inv = FindObjectOfType<PlayerInventory>();
            if (inv && inv.HasItem(requiredId)) return "E 摆放 " + displayName;
            return "这里还缺 " + displayName;
        }
    }

    public override void Interact(PlayerInventory inv)
    {
        if (!host || filled) return;
        if (!host.CanPlace(this, inv, out var reason))
        {
            var ui = Level2Director.Instance ? Level2Director.Instance.Ui : null;
            ui?.Note("归位", reason);
            return;
        }
        host.Place(this, inv);
    }
}

/// <summary>吉他归位点（二楼一号房 · 一号小房间的沙发旁边）。</summary>
public sealed class Level2GuitarSpot : L2Interactable
{
    public Level2Toys host;
    public bool filled;
    public float yaw;
    public Vector3 leanTo = Vector3.back;

    public override string Prompt
    {
        get
        {
            if (filled) return "吉他放好了";
            var inv = FindObjectOfType<PlayerInventory>();
            if (inv && inv.HasItem("l2_guitar")) return "E 把吉他靠在沙发上";
            return "沙发边的空位";
        }
    }

    public override void Interact(PlayerInventory inv)
    {
        if (!host || filled) return;
        var ui = Level2Director.Instance ? Level2Director.Instance.Ui : null;
        if (!inv.HasItem("l2_guitar")) { ui?.Note("吉他", "这里正好可以把吉他靠在沙发边。"); return; }
        host.PlaceGuitar(inv);
    }
}
