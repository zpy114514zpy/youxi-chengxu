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
        ui_Note("吉他", "角落里的吉他现在可以拿了 ——\n拿上它，去一号小房间的沙发上放好。");
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
        GameAudio.PlayPlace();
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

    /// <summary>同帧可用的落底：包围盒自己算（renderer.bounds 要等一帧才刷新）。</summary>
    static void AlignBottomFast(Transform toy, float surfaceY)
    {
        var b = WorldBoundsFast(toy);
        if (b.size.y < .001f) return;
        Vector3 p = toy.position;
        p.y += surfaceY - b.min.y;
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
        Ui?.Say("吉他", "我得把吉他也送回二楼一号房间 ——\n搁在一号小房间那张沙发上，琴背靠着靠背摆好。");
        BuildGuitarSpot();
    }

    void BuildGuitarSpot()
    {
        if (guitarSpot) return;

        chair = L2.Find("SM_Chair_B3");
        if (!chair) chair = L2.Find("SM_Chair_B1");
        if (!chair) Debug.LogWarning("[谜题二] 一号小房间里找不到沙发 SM_Chair_B3");

        Vector3 seat;
        Vector3 backDir;
        float yaw;
        FindGuitarOnChair(chair, out seat, out backDir, out yaw);

        var host = new GameObject("吉他放置点");
        host.transform.position = seat;

        // 交互盒包住座位。玩家的交互射线遇到不可交互的家具会继续往后找，
        // 所以盒子放在沙发里面也能瞄得到，不必凸到走道上挡人。
        var box = host.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, .34f, 0f);
        box.size = new Vector3(.80f, .70f, .80f);

        guitarSpot = host.AddComponent<Level2GuitarSpot>();
        guitarSpot.host = this;
        guitarSpot.yaw = yaw;
        guitarSpot.backDir = backDir;
        guitarSpot.seatY = seat.y;

        // 蓝色提示立在沙发前的地面上（座位正前方 0.62m）：
        // 摆在座位上会被扶手和靠背挡掉一半，站在门口根本看不见。
        var hintGo = new GameObject("放置提示");
        hintGo.transform.SetParent(host.transform, false);
        Vector3 hp = seat - backDir * .55f;
        hintGo.transform.position = new Vector3(hp.x, FloorY, hp.z);
        var hint = hintGo.AddComponent<Level2SpotHint>();
        hint.Build(.16f, .60f);
        guitarSpot.hint = hint;

        Debug.Log($"[谜题二] 吉他放置点 {L2.Fmt(seat)} 坐垫面 y={seat.y:F3} 靠背朝 {L2.Fmt(backDir)}");
    }

    // 一号小房间里那张扶手椅（沙发）。
    Transform chair;

    /// <summary>
    /// 量出这张沙发的「坐垫面」和「靠背方向」。
    /// 琴不是立在椅子旁边，而是搁在座位上、琴背搭着靠背 —— 现实里随手把琴放在沙发上的样子。
    /// 靠背方向用“整把椅子里最高的那块渲染器相对椅子中心的水平方向”来认，不依赖 prefab 的朝向。
    /// </summary>
    void FindGuitarOnChair(Transform chair, out Vector3 seat, out Vector3 backDir, out float yaw)
    {
        var b = chair ? L2.BoundsOf(chair)
            : new Bounds(new Vector3(-17.10f, 3.60f, -91.10f), new Vector3(1.05f, 1.15f, 1.05f));
        Vector3 c = b.center;

        // 靠背方向：这张沙发只有一块整体网格（SM_Chair_B1），
        // “哪个方向的渲染器更高”这类判据在这里全失效，所以直接看网格顶点：
        // 把高过座面一大截的顶点（靠背 + 两侧扶手）求个平均位置，
        // 扶手左右对称会互相抵消，剩下的就是靠背的方向。
        backDir = Vector3.back;
        if (chair)
        {
            float highCut = b.min.y + b.size.y * .68f;
            Vector3 meshSum = Vector3.zero;
            int meshN = 0;
            foreach (var mf in chair.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf || !mf.sharedMesh) continue;
                var m = mf.transform.localToWorldMatrix;
                var vs = mf.sharedMesh.vertices;
                for (int i = 0; i < vs.Length; i += 2)          // 隔一个取一个，够用
                {
                    Vector3 w = m.MultiplyPoint3x4(vs[i]);
                    if (w.y < highCut) continue;
                    meshSum += w;
                    meshN++;
                }
            }

            if (meshN > 8)
            {
                Vector3 dd = meshSum / meshN - c;
                dd.y = 0f;
                if (dd.sqrMagnitude > .0025f) backDir = dd.normalized;
            }
            else
            {
                // 顶点也取不到（网格不在 MeshFilter 上）时，退回“离哪面墙最近”。
                float nearest = 99f;
                foreach (var d in new[]
                {
                    Vector3.forward, Vector3.back, Vector3.right, Vector3.left,
                    new Vector3(.707f, 0f, .707f), new Vector3(-.707f, 0f, .707f),
                    new Vector3(.707f, 0f, -.707f), new Vector3(-.707f, 0f, -.707f),
                })
                {
                    float ext = Mathf.Abs(d.x) * b.extents.x + Mathf.Abs(d.z) * b.extents.z;
                    Vector3 p = new Vector3(c.x + d.x * (ext + .06f), b.min.y + .12f, c.z + d.z * (ext + .06f));
                    if (!Physics.Raycast(p, d, out var wh, 1.8f,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                    if (!IsWall(wh.transform.name)) continue;
                    if (wh.distance < nearest) { nearest = wh.distance; backDir = d; }
                }
            }
            Debug.Log($"[谜题二] 靠背判定：顶点 {meshN} 个 → 方向 {L2.Fmt(backDir)}");
        }
        backDir.y = 0f;
        if (backDir.sqrMagnitude < .01f) backDir = Vector3.back;
        backDir.Normalize();

        float extent = Mathf.Abs(backDir.x) * b.extents.x + Mathf.Abs(backDir.z) * b.extents.z;

        // 坐垫面：从座位前部正上方竖着往下打，第一个命中这张椅子的点就是座面高度。
        Vector3 probe = new Vector3(c.x - backDir.x * extent * .38f, b.max.y + .30f,
                                    c.z - backDir.z * extent * .38f);
        float seatY = b.min.y + b.size.y * .38f;
        if (Physics.Raycast(probe, Vector3.down, out var hit, b.size.y + 1.5f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            bool mine = chair && hit.transform && (hit.transform == chair || hit.transform.IsChildOf(chair));
            if (mine && hit.point.y > b.min.y + .10f && hit.point.y < b.min.y + b.size.y * .72f)
                seatY = hit.point.y;
        }

        seat = new Vector3(probe.x, seatY, probe.z);
        yaw = Mathf.Atan2(-backDir.x, -backDir.z) * Mathf.Rad2Deg;   // 琴面朝外（开口方向）
        Debug.Log($"[谜题二] 沙发中心 {L2.Fmt(c)} 尺寸 {L2.Fmt(b.size)} → 坐垫面 y={seatY:F3}，靠背朝 {L2.Fmt(backDir)}");
    }

    static bool IsWall(string n) =>
        !string.IsNullOrEmpty(n) && (n.Contains("Wall") || n.Contains("Corner"));

    public void PlaceGuitar(PlayerInventory inv)
    {
        var item = inv.TakeItem("l2_guitar");
        if (!item || !guitarSpot) return;
        AttachGuitar(item);
    }

    /// <summary>
    /// 把吉他竖着搁在沙发座面上、琴背靠着靠背（放置和读档共用这一段）。
    /// 目标是现实里“随手把琴往沙发上一靠”的样子：
    ///   · 琴身（大的那一头）在下，坐在坐垫面上；
    ///   · 琴颈朝上，略微后仰，琴背贴着靠背；
    ///   · 琴面朝向房间，不横穿左右扶手。
    /// </summary>
    void AttachGuitar(PickupItem item)
    {
        if (!item || !guitarSpot) return;

        item.transform.SetParent(null, true);
        item.gameObject.SetActive(true);

        Vector3 backDir = guitarSpot.backDir;
        backDir.y = 0f;
        if (backDir.sqrMagnitude < .001f) backDir = Vector3.forward;
        backDir.Normalize();

        // 这个模型是“竖着靠墙”摆的：琴身在下、琴颈朝上略向后倾，琴面朝外、琴背朝里。
        // 只绕竖直轴把“琴背”转到椅子的靠背方向，那点后仰原样保留 —— 正好就是“靠着”的样子。
        // （不要再去反解视觉层的局部旋转：那 61° 是这个模型转正所必需的，抹掉琴就躺平了。）
        Quaternion orig = item.transform.rotation;
        float delta = Mathf.Atan2(backDir.x, backDir.z) * Mathf.Rad2Deg - 180f;
        item.transform.rotation = Quaternion.AngleAxis(delta, Vector3.up) * orig;

        var rawB = WorldBoundsFast(item.transform);
        Debug.Log($"[谜题二] 吉他姿态：琴背 -Z → 靠背 {L2.Fmt(backDir)}（转 {delta:F0}°），" +
                  $"包围盒 {L2.Fmt(rawB.size)} 高 {rawB.size.y:F2}m");

        // 落点：座位点往靠背方向挪 30cm，挪到坐垫中间偏后。
        // 原来的座位点取在坐垫前缘（离靠背 0.34m），琴有一半在坐垫外面，
        // 底部就悬在椅子前面的空气里 —— 这才是“虚空放置”的真正原因。
        Vector3 seat = guitarSpot.transform.position + backDir * .30f;

        // 先把碰撞体关掉，再往下量坐垫：不然射线第一个打到的就是琴自己。
        foreach (var c in item.GetComponentsInChildren<Collider>(true)) c.enabled = false;

        item.transform.position = seat;
        AlignBottomFast(item.transform, SeatSurfaceAt(seat));
        Physics.SyncTransforms();

        // 贴靠 + 重新落座放到下一帧做：这一帧里 renderer.bounds 还是旧值。
        StartCoroutine(ContactSettle(item.transform, backDir));

        var outline = item.GetComponentInChildren<ProximityOutline>(true);
        if (outline) outline.enabled = false;
        var pick = item.GetComponentInChildren<PickupItem>(true);
        if (pick) pick.enabled = false;

        guitarSpot.filled = true;
        if (guitarSpot.hint) guitarSpot.hint.SetVisible(false);
        GameAudio.PlayPlace();
        OnAllDone();
    }

    /// <summary>单个渲染器立刻能用的世界包围盒（局部包围盒 × 变换矩阵，不等一帧）。</summary>
    static Bounds FastBounds(Renderer r)
    {
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
        return new Bounds(c, e * 2f);
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
            var nb = FastBounds(r);
            if (!has) { total = nb; has = true; } else total.Encapsulate(nb);
        }
        return has ? total : new Bounds(t.position, Vector3.zero);
    }

    /// <summary>这个位置正下方的坐垫面高度（压 1.2cm 进软垫）。打不到椅子就退回座位点的高度。</summary>
    float SeatSurfaceAt(Vector3 p)
    {
        float top = guitarSpot.seatY + .55f;
        if (chair && Physics.Raycast(new Vector3(p.x, top, p.z), Vector3.down, out var hit, 1.6f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.transform && (hit.transform == chair || hit.transform.IsChildOf(chair)))
                return hit.point.y - .012f;
        }
        return guitarSpot.seatY - .012f;
    }

    /// <summary>
    /// 让琴坐在坐垫上、琴背搭住靠背。
    /// 不再拿“整把琴的包围盒”去估算背面 —— 琴颈本来就向后仰，包围盒会把琴颈那截
    /// 也算成“琴背伸出去”，于是把整把琴一路往后推，最后推到坐垫外面吊在半空。
    /// 这里按高度一层一层采网格顶点，看每一层琴真正探出去多远，再跟靠背的实际位置比。
    /// </summary>
    System.Collections.IEnumerator ContactSettle(Transform t, Vector3 backDir)
    {
        yield return null;
        if (!t) yield break;

        Vector3 center = guitarSpot.transform.position;
        float moved = 0f;

        for (int iter = 0; iter < 3; iter++)
        {
            float surface = SeatSurfaceAt(t.position);
            AlignBottomFast(t, surface);              // 压进软垫 1.2cm，看着是“坐实”而不是浮着
            Physics.SyncTransforms();

            float bottom = WorldBoundsFast(t).min.y;
            float shift = 0f;
            foreach (float h in new[] { .10f, .24f, .38f, .52f, .66f, .78f })
            {
                float y = bottom + h;
                if (!Physics.Raycast(new Vector3(center.x, y, center.z), backDir, out var hit, 1.20f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                if (!chair || !hit.transform || !(hit.transform == chair || hit.transform.IsChildOf(chair))) continue;

                float reach = MaxReachAt(t, backDir, y, center);
                if (float.IsNegativeInfinity(reach)) continue;
                float over = reach - (hit.distance - .012f);   // 留 1.2cm，别顶穿椅背
                if (over > shift) shift = over;
            }

            if (shift <= .002f) break;
            float step = Mathf.Min(shift, .10f);               // 退让量封顶，绝不让琴退出坐垫
            moved += step;
            t.position -= backDir * step;
            AlignBottomFast(t, SeatSurfaceAt(t.position));
            Physics.SyncTransforms();
            yield return null;
        }

        var f = WorldBoundsFast(t);
        Debug.Log($"[谜题二] 吉他上了沙发 {L2.Fmt(t.position)} 尺寸 {L2.Fmt(f.size)} " +
                  $"底 y={f.min.y:F3}（坐垫面 {SeatSurfaceAt(t.position):F3}），离靠背退让 {moved:F3}m");
    }

    /// <summary>这一层高度上，琴沿 dir 最远探到哪儿（相对 origin）。用网格顶点算，比包围盒准得多。</summary>
    static float MaxReachAt(Transform t, Vector3 dir, float y, Vector3 origin)
    {
        float best = float.NegativeInfinity;
        foreach (var mf in t.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!mf || !mf.sharedMesh) continue;
            var m = mf.transform.localToWorldMatrix;
            var vs = mf.sharedMesh.vertices;
            int step = Mathf.Max(1, vs.Length / 4000);
            for (int i = 0; i < vs.Length; i += step)
            {
                Vector3 w = m.MultiplyPoint3x4(vs[i]);
                if (Mathf.Abs(w.y - y) > .055f) continue;
                float p = (w.x - origin.x) * dir.x + (w.z - origin.z) * dir.z;
                if (p > best) best = p;
            }
        }
        return best;
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

        // 读档时可能根本没走过“四个玩偶归位”，吉他还是光模型、没有拾取外壳。
        // 先把外壳补上，否则下面按 id 找 PickupItem 会拿到 null，吉他既拿不起来也放不回去。
        if (guitarTaken && guitar && !guitar.GetComponentInChildren<PickupItem>(true))
            guitar = L2.WrapPickup(guitar, "破损的吉他", "l2_guitar", "乐器", .3f, .3f,
                "断了两根弦的旧吉他，琴身上还有小时候贴的贴纸。");

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

        // 拿到过吉他就要把放置点建出来 —— 否则“拿了没放”的存档读回来，玩家找不到地方放。
        if (guitarTaken) BuildGuitarSpot();
        if (guitarPlaced) RestoreGuitar();

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
        // 读档时 guitar 已经被换成 WrapPickup 建的外壳，PickupItem 挂在外壳自己身上，
        // GetComponentInChildren 找不到（会直接 return，吉他就不见了）。
        var pick = guitar.GetComponentInChildren<PickupItem>(true);
        if (!pick) pick = guitar.GetComponentInParent<PickupItem>();
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

        // 吉他：四个玩偶摆好、琴也拿到手了，沙发上那个位置才亮。
        if (guitarSpot && guitarSpot.hint)
            guitarSpot.hint.SetVisible(placedAll && guitarTaken && !guitarPlaced);
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

/// <summary>吉他归位点（二楼一号房 · 一号小房间的那张沙发）。</summary>
public sealed class Level2GuitarSpot : L2Interactable
{
    public Level2Toys host;
    public bool filled;
    public float yaw;
    public Vector3 backDir = Vector3.back;   // 指向靠背的水平方向
    public float seatY = 3.62f;              // 坐垫面高度
    public Level2SpotHint hint;

    public override string Prompt
    {
        get
        {
            if (filled) return "吉他放好了";
            var inv = FindObjectOfType<PlayerInventory>();
            if (inv && inv.HasItem("l2_guitar")) return "E 把吉他放到沙发上";
            return "沙发上的空位";
        }
    }

    public override void Interact(PlayerInventory inv)
    {
        if (!host || filled) return;
        var ui = Level2Director.Instance ? Level2Director.Instance.Ui : null;
        if (!inv.HasItem("l2_guitar")) { ui?.Note("吉他", "把吉他搁在这张沙发上、靠着靠背摆好。"); return; }
        host.PlaceGuitar(inv);
    }
}
