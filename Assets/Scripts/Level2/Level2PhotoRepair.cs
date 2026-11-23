using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 谜题一 · 修补全家福。
/// 二楼二号房间里散着三张相片残片（场景里的 对象002 / 对象003 / 对象004），
/// 捡齐后到墙上那幅全家福（wall_picture_3）前，按 E 一张一张补回去。
/// 补完也不会变回鲜艳 —— 只是把轮廓拼回来了，这是这一段的核心表达。
/// </summary>
public sealed class Level2PhotoRepair : MonoBehaviour
{
    const int PieceCount = 3;

    // 场景里三张残片的节点名（都在 相框/ 下面）。
    static readonly string[] PieceNodes = { "对象002", "对象003", "对象004" };
    static readonly string[] PieceNames = { "相片残片①", "相片残片②", "相片残片③" };

    Level2Ui Ui => Level2Director.Instance ? Level2Director.Instance.Ui : null;

    readonly List<Transform> pieces = new List<Transform>();
    Transform picture;
    Level2PhotoFrame frame;
    int placed, lastHave;
    public int Placed => placed;
    public bool Repaired { get; private set; }

    public void Setup()
    {
        BuildPieces();
        BuildFrame();
    }

    // ---------------- 残片 ----------------

    void BuildPieces()
    {
        for (int i = 0; i < PieceNodes.Length; i++)
        {
            var t = L2.Find(PieceNodes[i]);
            if (!t) { Debug.LogWarning("[谜题一] 找不到残片 " + PieceNodes[i]); continue; }

            // 已经包过就不再重复包（编辑器里重进 Play 会重跑装配）。
            if (t.parent && t.parent.name.StartsWith("道具_")) { pieces.Add(t.parent); continue; }

            var host = L2.WrapPickup(t, PieceNames[i], "l2_photo_piece_" + (i + 1), "相片残片",
                .16f, .05f, "一张泛黄的全家福残片，边角已经磨毛了。");
            // 房间里本来就散着好几片白纸，残片得在更远处就勾出蓝边，玩家才找得到。
            if (host) L2.AddBlueOutline(host, L2.OutlineBlue, L2.OutlineWidth, 6.5f);
            if (host) pieces.Add(host);
        }
        Debug.Log($"[谜题一] 可拾取残片 {pieces.Count} 张");
    }

    // ---------------- 相框 ----------------

    void BuildFrame()
    {
        picture = L2.Find("wall_picture_3");
        if (!picture)
        {
            Debug.LogWarning("[谜题一] 找不到全家福 wall_picture_3");
            return;
        }

        // 相框挂在东墙上，本身只有 0.37×0.54，而且父级缩放极大；
        // 交互盒单独建一个世界缩放的宿主，瞄准范围放宽一点，站在房间里抬头就能按到。
        var bounds = L2.BoundsOf(picture);
        var host = new GameObject("全家福·交互");
        host.transform.position = bounds.center + new Vector3(-.12f, 0f, 0f);
        host.transform.rotation = Quaternion.identity;

        var box = host.AddComponent<BoxCollider>();
        box.size = new Vector3(.55f, Mathf.Max(.5f, bounds.size.y + .18f), Mathf.Max(.62f, bounds.size.z + .18f));

        frame = host.AddComponent<Level2PhotoFrame>();
        frame.host = this;
        Debug.Log($"[谜题一] 全家福交互点 {L2.Fmt(host.transform.position)}");
    }

    // ---------------- 放置 ----------------

    public bool HasPiece(PlayerInventory inv)
    {
        if (!inv) return false;
        for (int i = 1; i <= PieceCount; i++)
            if (inv.HasItem("l2_photo_piece_" + i)) return true;
        return false;
    }

    public int CountInBag(PlayerInventory inv)
    {
        if (!inv) return 0;
        int n = 0;
        for (int i = 1; i <= PieceCount; i++)
            if (inv.HasItem("l2_photo_piece_" + i)) n++;
        return n;
    }

    public bool PlaceOne(PlayerInventory inv)
    {
        if (Repaired) return false;
        for (int i = 1; i <= PieceCount; i++)
        {
            var piece = inv.TakeItem("l2_photo_piece_" + i);
            if (!piece) continue;
            visuals.Add(piece.transform);
            AnchorToFrame(piece.transform, placed);
            placed++;
            if (placed >= PieceCount) Repaired = true;
            return true;
        }
        return false;
    }

    readonly List<Transform> visuals = new List<Transform>();

    /// <summary>把残片贴到相框表面，三片略微错开，看起来是拼回去的。</summary>
    void AnchorToFrame(Transform piece, int index)
    {
        if (!picture) return;
        var bounds = L2.BoundsOf(picture);

        // 相框朝房间的那一面（东墙，法线指向 -X）。
        Vector3 surface = bounds.center + new Vector3(-(bounds.size.x * .5f + .006f), 0f, 0f);
        Vector3[] offsets =
        {
            new Vector3(0f,  .055f, -.045f),
            new Vector3(0f, -.045f,  .040f),
            new Vector3(0f,  .010f, -.005f),
        };
        Vector3 target = surface + offsets[Mathf.Clamp(index, 0, 2)];

        piece.SetParent(null, true);
        piece.gameObject.SetActive(true);
        piece.position = target;
        // 纸片本体是平的，正面朝 +Y；这里把它立起来，正面朝房间（-X）。
        piece.rotation = Quaternion.LookRotation(Vector3.left, Vector3.up);

        // 双面显示：碎片翻过来也看得见，不会出现“补了一片却什么都没有”。
        foreach (var r in piece.GetComponentsInChildren<Renderer>(true))
        {
            if (!r) continue;
            foreach (var m in r.materials)
                if (m && m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
        }

        // 贴到相框上就不再是可拾取的散落物了。
        foreach (var c in piece.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        var outline = piece.GetComponentInChildren<ProximityOutline>(true);
        if (outline) outline.enabled = false;
        var pick = piece.GetComponentInChildren<PickupItem>(true);
        if (pick) pick.enabled = false;
    }

    public void OnRepaired()
    {
        Debug.Log("[谜题一] 三张残片补完");
        if (!Level2Director.Instance) return;
        var ui = Level2Director.Instance.Ui;

        ui.Say("内心独白", "照片再也回不到从前鲜亮的模样。\n发生过的事不会消失，但我不必把回忆彻底撕碎。");
        ui.Say("谜题一完成", "恭喜完成谜题一。");
        ui.Say("新的目标", "我的玩偶与吉他还在这里，我需要把他们放在“二楼一号房间”里。");

        if (Level2Director.Instance.Toys) Level2Director.Instance.Toys.Unlock();
    }

    public void ReadAgain()
    {
        var ui = Level2Director.Instance ? Level2Director.Instance.Ui : null;
        ui?.Say("全家福", "轮廓拼回来了，颜色还是旧的。\n像被反复折叠过又重新摊平的纸。");
    }

    void Update()
    {
        if (pieces.Count == 0) return;
        var inv = Level2Director.Instance ? Level2Director.Instance.GetComponentInChildren<PlayerInventory>() : null;
        if (!inv) return;
        int have = CountInBag(inv);
        if (have != lastHave)
        {
            if (have > 0 && have < PieceCount && !Repaired)
                Ui?.Note("相片残片", $"已经找到 {have} / {PieceCount} 张相片残片，还有 {PieceCount - have} 张散在房间里。");
            lastHave = have;
        }
    }
}

/// <summary>墙上的全家福：玩家按 E 把背包里的残片补上去。</summary>
public sealed class Level2PhotoFrame : L2Interactable
{
    public Level2PhotoRepair host;
    PlayerInventory inventory;

    public override string Prompt
    {
        get
        {
            if (!host) return "E 看看这幅画";
            if (host.Repaired) return "E 看看这张全家福";
            if (!inventory) inventory = FindObjectOfType<PlayerInventory>();
            int have = host.CountInBag(inventory);
            return have > 0 ? "E 补上相片残片" : "这幅全家福缺了三块";
        }
    }

    public override void Interact(PlayerInventory inv)
    {
        if (!host) return;
        if (host.Repaired) { host.ReadAgain(); return; }

        if (!host.HasPiece(inv))
        {
            // 没有残片时也按 E：给一句提示，而不是毫无反应。
            var ui = Level2Director.Instance ? Level2Director.Instance.Ui : null;
            ui?.Note("全家福", "还差几块相片残片。它们散在这个房间里 —— 地上、桌角、地毯上。");
            return;
        }

        if (host.PlaceOne(inv))
        {
            var ui = Level2Director.Instance ? Level2Director.Instance.Ui : null;
            if (!host.Repaired) ui?.Note("修补全家福", "补上了一块。还差 " + (3 - host.Placed) + " 块。");
            else host.OnRepaired();
        }
    }
}
