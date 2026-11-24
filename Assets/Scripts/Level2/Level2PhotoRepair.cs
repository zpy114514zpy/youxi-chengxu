using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 谜题一 · 修补全家福。
/// 二楼二号房间里散着三张相片残片（场景里的 对象002 / 对象003 / 对象004），
/// 捡齐后到墙上那幅全家福（wall_picture_3）前，按 E 一块一块补回去 ——
/// 三块按撕口拼起来正好是一整张全家福，不是三张小图糊上去。
/// 补完也不会变回鲜艳：只是把轮廓拼回来了，这是这一段的核心表达。
/// </summary>
public sealed class Level2PhotoRepair : MonoBehaviour
{
    const int PieceCount = 3;

    // 场景里三张残片的节点名（都在 相框/ 下面）。
    static readonly string[] PieceNodes = { "对象002", "对象003", "对象004" };
    static readonly string[] PieceNames = { "相片残片①", "相片残片②", "相片残片③" };

    // 三块残片在整张全家福里的 UV 区间（左, 右, 下, 上），由撕口决定；四周留了 46px 透明边给描边。
    static readonly Vector4[] PieceUV =
    {
        new Vector4(-0.0318f, 1.0318f, 0.3610f, 1.0424f),
        new Vector4(-0.0318f, 0.5449f, -0.0424f, 0.4687f),
        new Vector4(0.4655f, 1.0318f, -0.0424f, 0.4687f),
    };

    // 三块拼好之后，照片在相框上的显示尺寸（相框本身 0.54 宽 × 0.37 高）。
    const float PhotoW = .38f;
    const float PhotoH = .285f;
    // 碎片在房间里时的尺寸（比贴回去大一点，玩家才看得清）。
    const float SceneW = .46f;
    const float SceneH = .345f;
    const float PhotoInset = .035f;      // 从相框中心朝房间里伸出的距离

    // 旧照片的色调：不鲜亮，但也不是全黑。
    static readonly Color Tint = new Color(.62f, .58f, .52f, 1f);
    static readonly Color Edge = new Color(.42f, .74f, 1f, 1f);
    // 相框的“待修补”描边：黄色，和残片的蓝描边区分开 —— 玩家一进房间就知道墙上有东西要补。
    static readonly Color OutlineYellow = new Color(1f, .84f, .20f, 1f);
    // 描边粗细是世界单位（米）。相框 0.54×0.37，1cm 就是紧贴画框的一圈细线。
    const float BorderThickness = .010f;
    // 离画框正面多远（朝房间里）：1.5cm 让整圈浮在画框前面，既不会和画框/墙面抢深度，
    // 也不会被画框凸起的边沿挡住。
    const float BorderGap = .015f;

    Level2Ui Ui => Level2Director.Instance ? Level2Director.Instance.Ui : null;

    readonly List<Transform> pieces = new List<Transform>();
    Transform picture;
    Transform border;                    // 黄描边（四条细边），补完就关掉
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
            var paper = L2.Find(PieceNodes[i]);
            if (!paper) { Debug.LogWarning("[谜题一] 找不到残片 " + PieceNodes[i]); continue; }

            // 已经包过就不再重复包（编辑器里重进 Play 会重跑装配）。
            if (paper.parent && paper.parent.name.StartsWith("道具_")) { pieces.Add(paper.parent); continue; }

            var pb = L2.BoundsOf(paper);
            var holder = MakePieceVisual(i, pb);
            if (!holder) continue;

            // 原来那张白纸只是占位，换成真正的照片碎片。
            paper.gameObject.SetActive(false);

            var host = L2.WrapPickup(holder, PieceNames[i], "l2_photo_piece_" + (i + 1), "相片残片",
                .16f, .04f, "一张泛黄的全家福残片，边角已经磨毛了。", false);
            if (!host) continue;

            pieces.Add(host);
        }
        Debug.Log($"[谜题一] 可拾取残片 {pieces.Count} 张");
    }

    /// <summary>一块照片碎片：下面一层蓝色描边、上面一层照片，平铺在原地。</summary>
    Transform MakePieceVisual(int index, Bounds paper)
    {
        Vector4 uv = PieceUV[index];
        float w = SceneW * (uv.y - uv.x);
        float h = SceneH * (uv.w - uv.z);
        float[] yaw = { 16f, -24f, 38f };

        var holder = new GameObject("残片视觉_" + (index + 1));
        holder.transform.SetPositionAndRotation(
            new Vector3(paper.center.x, paper.max.y + .006f, paper.center.z),
            Quaternion.Euler(0f, yaw[index], 0f));

        var photoMat = PhotoMaterial("Level2/photo_piece_" + (index + 1), Tint);
        var glowMat = PhotoMaterial("Level2/photo_piece_" + (index + 1) + "_glow", Color.white, true);

        var glow = MakeQuad("描边", holder.transform, new Vector3(0f, -.0015f, 0f),
            new Vector2(w, h), glowMat, Quaternion.Euler(-90f, 0f, 0f));
        var photo = MakeQuad("照片", holder.transform, Vector3.zero,
            new Vector2(w, h), photoMat, Quaternion.Euler(-90f, 0f, 0f));
        if (!glow || !photo) return null;
        return holder.transform;
    }

    Transform MakeQuad(string name, Transform parent, Vector3 localPos, Vector2 size,
        Material material, Quaternion localRot)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());
        var t = go.transform;
        t.SetParent(parent, false);
        t.localPosition = localPos;
        t.localRotation = localRot;
        t.localScale = new Vector3(size.x, size.y, 1f);
        var r = go.GetComponent<Renderer>();
        if (material) r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return t;
    }

    Material PhotoMaterial(string resource, Color tint, bool soft = false)
    {
        var tex = Resources.Load<Texture2D>(resource);
        if (!tex) Debug.LogWarning("[谜题一] 缺贴图 " + resource);
        var m = L2.Unlit(tint, soft);
        if (tex)
        {
            m.SetTexture("_BaseMap", tex);
            // 贴回相框时 quad 的 +X 与玩家右手相反，水平翻一次照片才是正着的。
            m.SetTextureScale("_BaseMap", new Vector2(-1f, 1f));
            m.SetTextureOffset("_BaseMap", new Vector2(1f, 0f));
        }
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
        if (soft)
        {
            // 描边要柔柔地淡出去，走混合；队列压在照片（2450）下面，才不会糊住照片本身。
            m.renderQueue = 2400;
        }
        else
        {
            // 撕口是透明通道做的，必须走 AlphaTest，否则整块矩形都会挡住下面的描边。
            if (m.HasProperty("_AlphaClip")) m.SetFloat("_AlphaClip", 1f);
            if (m.HasProperty("_Cutoff")) m.SetFloat("_Cutoff", .35f);
            m.EnableKeyword("_ALPHATEST_ON");
            m.renderQueue = 2450;
        }
        return m;
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

        // 黄色描边：这幅全家福是谜题一的目标，暗房间里不描一下玩家容易找不到。
        // 不能用 ProximityOutline —— 它是沿法线膨胀的外壳 + 加法混合，而相框几乎是一块平板，
        // 外壳整片盖在照片上，结果整幅画都在发黄光。这里自己拿四条细边圈一圈。
        BuildBorder(bounds);

        Debug.Log($"[谜题一] 全家福交互点 {L2.Fmt(host.transform.position)}，画框 {L2.Fmt(bounds.size)}，黄色描边 {(border ? "已添加" : "添加失败")}");
    }

    /// <summary>
    /// 在相框四周拼四条细黄条（实体薄方条，不是单面 Quad）。
    /// 之前用 Quad 完全看不见：Unity 的 Quad 原语法线朝 local -Z，而 URP Unlit 默认背面剔除，
    /// 从房间里看正好是背面，整圈被剔掉了。方条是实体，任何角度都在。
    /// </summary>
    void BuildBorder(Bounds b)
    {
        if (b.size.y <= .01f || b.size.z <= .01f)
        {
            Debug.LogWarning($"[谜题一] 相框尺寸异常 {L2.Fmt(b.size)}，跳过描边");
            return;
        }

        float halfH = b.size.y * .5f;
        float halfW = b.size.z * .5f;
        float t = BorderThickness;
        float x = b.min.x - BorderGap;                  // 房间在 -X 侧，整圈浮在画框前面
        var mat = L2.Unlit(new Color(OutlineYellow.r, OutlineYellow.g, OutlineYellow.b, 1f));

        border = new GameObject("全家福·黄色描边").transform;

        // 上 / 下：横条铺满（含两个角），截面就是线的粗细
        for (int i = 0; i < 2; i++)
        {
            float s = i == 0 ? 1f : -1f;
            L2.Box("描边" + (i + 1), border,
                new Vector3(x, b.center.y + s * (halfH + t * .5f), b.center.z),
                new Vector3(t, t, 2f * halfW + 2f * t), mat, Quaternion.identity);
        }
        // 左 / 右：竖条接满上下两条之间
        for (int i = 0; i < 2; i++)
        {
            float s = i == 0 ? 1f : -1f;
            L2.Box("描边" + (i + 3), border,
                new Vector3(x, b.center.y, b.center.z + s * (halfW + t * .5f)),
                new Vector3(t, 2f * halfH, t), mat, Quaternion.identity);
        }

        Debug.Log($"[谜题一] 黄色描边 4 条，外框 {b.size.z + t * 2f:F2}×{b.size.y + t * 2f:F2}m，" +
                  $"浮在 x={x:F2}（画框 {L2.Fmt(b.min)} ~ {L2.Fmt(b.max)}）");
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
            GameAudio.PlayPlace();
            return true;
        }
        return false;
    }

    readonly List<Transform> visuals = new List<Transform>();

    /// <summary>把碎片按它在整张照片里的位置贴回相框，三块严丝合缝拼成一张全家福。</summary>
    void AnchorToFrame(Transform host, int index)
    {
        if (!picture) return;
        var bounds = L2.BoundsOf(picture);

        Vector4 uv = PieceUV[Mathf.Clamp(index, 0, 2)];
        float w = PhotoW * (uv.y - uv.x);
        float h = PhotoH * (uv.w - uv.z);
        float uMid = (uv.x + uv.y) * .5f;
        float vMid = (uv.z + uv.w) * .5f;

        // 相框朝房间的那一面（东墙，法线指向 -X）。
        Vector3 face = Vector3.left;
        Vector3 center = bounds.center + face * PhotoInset;
        // 玩家朝 +X 看墙时，画面左侧是 +Z，所以 u=0 对应 +Z。
        Vector3 target = center
                         + Vector3.up * ((vMid - .5f) * PhotoH)
                         + Vector3.forward * ((.5f - uMid) * PhotoW);

        host.SetParent(null, true);
        host.gameObject.SetActive(true);
        host.SetPositionAndRotation(target, Quaternion.LookRotation(face, Vector3.up));
        host.localScale = Vector3.one;

        // host → holder → [描边, 照片]：贴到相框后改成正面朝房间，两层前后错开 2mm 防闪烁。
        var holder = host.childCount > 0 ? host.GetChild(0) : null;
        if (holder)
        {
            holder.localPosition = Vector3.zero;
            holder.localRotation = Quaternion.identity;
            holder.localScale = Vector3.one;
            for (int i = 0; i < holder.childCount; i++)
            {
                var q = holder.GetChild(i);
                q.localRotation = Quaternion.identity;
                q.localScale = new Vector3(w, h, 1f);
                q.localPosition = new Vector3(0f, 0f, i == 0 ? 0f : .002f);
            }
        }

        // 贴回相框后不再是可拾取的散落物。
        foreach (var c in host.GetComponentsInChildren<Collider>(true)) c.enabled = false;
        var outline = host.GetComponentInChildren<ProximityOutline>(true);
        if (outline) outline.enabled = false;
        var pick = host.GetComponentInChildren<PickupItem>(true);
        if (pick) pick.enabled = false;
    }

    public void OnRepaired()
    {
        Debug.Log("[谜题一] 三张残片补完");
        // 补完就不再是“待修补目标”，黄描边关掉，免得整关一直亮着。
        if (border) { border.gameObject.SetActive(false); Debug.Log("[谜题一] 补完，关闭黄色描边"); }
        if (!Level2Director.Instance) return;
        var ui = Level2Director.Instance.Ui;

        // 补完给三页：先道喜，再说下一步，最后留一句余韵。
        ui.Say("谜题一完成", "恭喜完成相框的修补，全家福又回来啦！");
        ui.Say("新的目标", "接下来请完成对玩偶和吉他的整理归位。");
        ui.Say("家的样子", "期待看到被你整理好的家哦！");

        if (Level2Director.Instance.Toys) Level2Director.Instance.Toys.Unlock();
        // 出去外面那扇门，到这刻才开。
        if (Level2Director.Instance.Doors) Level2Director.Instance.Doors.OpenExit();
    }

    public void ReadAgain()
    {
        var ui = Level2Director.Instance ? Level2Director.Instance.Ui : null;
        ui?.Say("全家福", "轮廓拼回来了，颜色还是旧的。\n像被反复折叠过又重新摊平的纸。");
    }

    // ---------------- 存档 ----------------

    public int PlacedCount => placed;

    /// <summary>
    /// 读档：把房间里还没捡的残片收起来，再按已经补上的块数把照片重新贴回相框。
    /// 存档里只记一个数字，画面全部按同一套 UV 重算，不会错位。
    /// </summary>
    public void RestoreProgress(int count)
    {
        if (count <= 0) return;
        foreach (var p in pieces)
            if (p) p.gameObject.SetActive(false);

        for (int i = 0; i < Mathf.Min(count, PieceCount); i++)
        {
            var holder = MakePieceVisual(i, new Bounds(Vector3.zero, Vector3.one));
            if (!holder) continue;
            var host = new GameObject("已归位残片" + (i + 1));
            holder.SetParent(host.transform, true);
            AnchorToFrame(host.transform, i);
            visuals.Add(host.transform);
        }
        placed = Mathf.Min(count, PieceCount);
        Repaired = placed >= PieceCount;
        if (Repaired && border) border.gameObject.SetActive(false);   // 读档时已经补完，黄描边别再亮
        Debug.Log($"[谜题一] 读档恢复：已补 {placed} / {PieceCount} 块");
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
