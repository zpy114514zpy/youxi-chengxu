using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 谜题二 · 归位残破玩具。
/// 流程：在二楼二号房间捡齐 4 个破损玩偶 → 去三号房洗手台按 E 一个个洗干净
///      → 摆回二楼一号房间的柜面 / 凳面 / 床 → 再去拿吉他，放到一号房间西南角的拐角里。
/// 吉他一开始是“不可交互”的：4 个玩偶没全部摆好之前，它连拾取提示都不会出现。
/// </summary>
public sealed class Level2Toys : MonoBehaviour
{
    // 场景里的玩偶节点名（按摆放点一一对应）。
    static readonly string[] ToyNodes = { "模型", "模型 (1)", "猪猪厨师3d模型", "模型 (2)" };
    static readonly string[] ToyNames = { "旧玩偶 · 一", "旧玩偶 · 二", "猪猪厨师", "旧玩偶 · 三" };
    static readonly string[] ToyIds = { "l2_toy_1", "l2_toy_2", "l2_toy_3", "l2_toy_4" };

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
            float top = b.max.y;
            AddSpot("柜面·左", ToyIds[0], ToyNames[0], new Vector3(b.center.x, top + .01f, b.center.z - .34f));
            AddSpot("柜面·右", ToyIds[1], ToyNames[1], new Vector3(b.center.x, top + .01f, b.center.z + .34f));
        }
        else Debug.LogWarning("[谜题二] 找不到五斗柜 BP_Drawer_A2_SM_Drawer_A1_0_129");

        // ② 凳面：猪猪厨师
        var chair = L2.Find("SM_Chair_C2");
        if (chair)
        {
            var b = L2.BoundsOf(chair);
            AddSpot("凳面", ToyIds[2], ToyNames[2], new Vector3(b.center.x, b.min.y + .47f, b.center.z));
        }
        else Debug.LogWarning("[谜题二] 找不到凳子 SM_Chair_C2");

        // ③ 床面：最后一个玩偶
        var bed = L2.Find("SM_Bed_A1");
        if (bed)
        {
            var b = L2.BoundsOf(bed);
            AddSpot("床面", ToyIds[3], ToyNames[3], new Vector3(b.center.x + .35f, b.min.y + .58f, b.center.z + .25f));
        }
        else Debug.LogWarning("[谜题二] 找不到床 SM_Bed_A1");
    }

    void AddSpot(string label, string id, string name, Vector3 position)
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
        ui_Note("吉他", "角落里的吉他现在可以拿了。");
        Debug.Log("[谜题二] 吉他已解锁拾取");
    }

    void ui_Note(string title, string body) => Ui?.Note(title, body);

    // ---------------- 状态查询 ----------------

    public bool IsWashed(string id) => washed.Contains(id);

    public int WashedCount => washed.Count;

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
            Ui?.Note("清洗玩偶", $"洗掉了一层灰（{washed.Count} / 4）。");
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

        var b = L2.BoundsOf(toy.transform);
        toy.transform.position += Vector3.up * (spot.transform.position.y - b.min.y);
        toy.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        foreach (var c in toy.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        var outline = toy.GetComponentInChildren<ProximityOutline>(true);
        if (outline) outline.enabled = false;
        var pick = toy.GetComponentInChildren<PickupItem>(true);
        if (pick) pick.enabled = false;

        spot.filled = true;
        int done = 0;
        foreach (var s in spots) if (s.filled) done++;
        if (done < spots.Count) Ui?.Note("归位", $"{spot.displayName} 放好了（{done} / {spots.Count}）。");
        else OnAllPlaced();
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
        Ui?.Say("吉他", "我需要把这个吉他也放到那个房间里。");
        BuildGuitarSpot();
    }

    void BuildGuitarSpot()
    {
        if (guitarSpot) return;
        var corner = L2.Find("SM_Corner_2Floor_A3");
        Vector3 at = corner ? corner.position : new Vector3(-24.7f, 3.2f, -91.8f);

        // 拐角在房子西南角。往房间里收进来一点：既贴着那个角，又不卡进两面墙里。
        // 只用“确实打到了二楼地板”的命中：墙角模型本身有 2.5m 高，从上面往下打会砸在
        // 墙角顶盖上（y≈3.9），吉他就会浮在半空。地板高度认 3.20 附近的一层。
        Vector3 target = new Vector3(at.x + .62f, 3.2f, at.z - .55f);
        if (Physics.Raycast(new Vector3(target.x, 4.6f, target.z), Vector3.down, out var hit, 1.8f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            && Mathf.Abs(hit.point.y - 3.2f) < .14f)
            target.y = hit.point.y;
        else Debug.Log("[谜题二] 拐角落到默认地板高度 3.20");

        var host = new GameObject("吉他放置点");
        host.transform.position = target;
        var box = host.AddComponent<BoxCollider>();
        // 中心抬到离地 0.3，碰撞盒贴地但不至于把人卡在墙角。
        box.center = Vector3.up * .3f;
        box.size = new Vector3(.55f, .55f, .55f);

        guitarSpot = host.AddComponent<Level2GuitarSpot>();
        guitarSpot.host = this;
        Debug.Log($"[谜题二] 吉他放置点 {L2.Fmt(target)}");
    }

    public void PlaceGuitar(PlayerInventory inv)
    {
        var item = inv.TakeItem("l2_guitar");
        if (!item || !guitarSpot) return;

        item.transform.SetParent(null, true);
        item.gameObject.SetActive(true);
        var b = L2.BoundsOf(item.transform);
        item.transform.position += Vector3.up * (guitarSpot.transform.position.y - b.min.y);
        // 靠着墙角立起来。
        item.transform.rotation = Quaternion.Euler(0f, 205f, 0f);

        foreach (var c in item.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        var outline = item.GetComponentInChildren<ProximityOutline>(true);
        if (outline) outline.enabled = false;
        var pick = item.GetComponentInChildren<PickupItem>(true);
        if (pick) pick.enabled = false;

        guitarSpot.filled = true;
        OnAllDone();
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

    // ---------------- 房间离开检测 ----------------

    void Update()
    {
        if (!unlocked || leftRoomNotified) return;
        var player = Level2Director.Instance ? Level2Director.Instance.Player : null;
        if (!player) return;
        // 二号房间在 z 更小的一侧，穿过 A2 门（z≈-101.7）就算离开了这个房间。
        if (player.position.y > 2.8f && player.position.z > -101.0f && !placedAll)
        {
            leftRoomNotified = true;
            Ui?.Say("谜题二", "请完成谜题二：归位残破玩具。\n\n这个房间里的玩偶和吉他，本来都该在隔壁。");
        }

        if (!foundAll)
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
    public bool filled;

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

/// <summary>吉他归位点（二楼一号房间西南角的拐角）。</summary>
public sealed class Level2GuitarSpot : L2Interactable
{
    public Level2Toys host;
    public bool filled;

    public override string Prompt
    {
        get
        {
            if (filled) return "吉他放好了";
            var inv = FindObjectOfType<PlayerInventory>();
            if (inv && inv.HasItem("l2_guitar")) return "E 放下吉他";
            return "这个角落空着";
        }
    }

    public override void Interact(PlayerInventory inv)
    {
        if (!host || filled) return;
        var ui = Level2Director.Instance ? Level2Director.Instance.Ui : null;
        if (!inv.HasItem("l2_guitar")) { ui?.Note("角落", "这里适合放那把吉他。"); return; }
        host.PlaceGuitar(inv);
    }
}
