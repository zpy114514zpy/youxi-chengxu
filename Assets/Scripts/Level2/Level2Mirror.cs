using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 谜题三 · 闭环问答（客厅）。
/// 用的是一楼客厅里那面本来就有的旧穿衣镜（sm_forgottenmirror）：把它的镜面换成
/// 一块实时反射面（镜像相机 + RenderTexture），玩家站到镜前能看见自己 —— 那个还没长大的自己。
/// 镜前问答选完 → 桌上那个盒子自己弹开 → 打开盒子读信 → 黑幕白字收束。
/// </summary>
public sealed class Level2Mirror : MonoBehaviour
{
    Level2Ui Ui => Level2Director.Instance ? Level2Director.Instance.Ui : null;

    Transform mirrorRoot;
    Transform disc, adultDisc;
    Renderer discRenderer;
    Camera mirrorCam;
    RenderTexture rt;
    Material discMaterial, adultMaterial;
    Level2MirrorTalk talk;
    Level2LetterBox box;
    SkinnedMeshRenderer bodyShadow, bodySkin;

    Vector3 mirrorCenter, mirrorNormal;
    float halfW = .21f, halfH = .58f;

    bool unlocked, talked, answered, letterOpened, finished, adultShown;
    public bool Unlocked => unlocked;
    public bool Answered => answered;

    public void Setup()
    {
        BuildMirror();
        BuildBox();
    }

    public void Unlock()
    {
        unlocked = true;
        Debug.Log("[谜题三] 已解锁");
    }

    // ---------------- 客厅那面旧穿衣镜 ----------------

    void BuildMirror()
    {
        mirrorRoot = L2.Find("sm_forgottenmirror");
        if (!mirrorRoot)
        {
            Debug.LogWarning("[谜题三] 找不到 sm_forgottenmirror");
            return;
        }

        if (!MeasureMirror()) return;

        // 反射面：一块椭圆薄片，正好盖在原来的镜面上。
        int w = 512;
        int h = Mathf.RoundToInt(w * (halfH * 2f) / Mathf.Max(.01f, halfW * 2f));
        h = Mathf.Clamp(h, 256, 2048);
        rt = new RenderTexture(w, h, 24, RenderTextureFormat.Default) { hideFlags = HideFlags.DontSave };
        rt.Create();

        discMaterial = L2.Unlit(Color.white);
        discMaterial.SetTexture("_BaseMap", rt);
        // 镜面 quad 的 +X 与玩家右手相反，贴图水平翻一次才是正着的镜子。
        discMaterial.SetTextureScale("_BaseMap", new Vector2(-1f, 1f));
        discMaterial.SetTextureOffset("_BaseMap", new Vector2(1f, 0f));

        disc = L2.EllipseDisc("镜面", null, mirrorCenter + mirrorNormal * .006f, mirrorNormal,
            halfW * .985f, halfH * .985f, discMaterial);
        discRenderer = disc.GetComponent<Renderer>();
        discRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // 镜子里“长大之后”的那个身影：一层贴在镜面前的剪影，默认全透明。
        adultMaterial = L2.Unlit(new Color(1f, 1f, 1f, 0f), transparent: true);
        var adultTex = Level2Ui.Instance ? Level2Ui.Instance.AdultFigure : null;
        if (adultTex)
        {
            adultMaterial.SetTexture("_BaseMap", adultTex);
            adultMaterial.SetTextureScale("_BaseMap", new Vector2(1f, 1f));
        }
        adultDisc = L2.EllipseDisc("镜中人影", null, mirrorCenter + mirrorNormal * .012f, mirrorNormal,
            halfW * .72f, halfH * .80f, adultMaterial);
        adultDisc.gameObject.SetActive(false);

        // 交互盒：站在镜子前按 E 就能触发。
        var host = new GameObject("落地镜·交互");
        host.transform.SetPositionAndRotation(mirrorCenter + mirrorNormal * .55f,
            Quaternion.LookRotation(mirrorNormal, Vector3.up));
        var col = host.AddComponent<BoxCollider>();
        col.size = new Vector3(halfW * 2f + .50f, halfH * 2f, .80f);

        talk = host.AddComponent<Level2MirrorTalk>();
        talk.host = this;

        // 客厅是整间屋子情绪的落点：给镜前一小片柔和暖光，别让这里冷得像走廊尽头。
        var glow = new GameObject("客厅暖光");
        glow.transform.position = mirrorCenter + mirrorNormal * .7f + Vector3.up * .75f;
        var lamp = glow.AddComponent<Light>();
        lamp.type = LightType.Point;
        lamp.color = new Color(1f, .82f, .60f);
        lamp.range = 3.2f;
        lamp.intensity = .55f;
        lamp.shadows = LightShadows.Soft;

        BuildMirrorCamera();
        Debug.Log($"[谜题三] 穿衣镜 中心={L2.Fmt(mirrorCenter)} 法线={L2.Fmt(mirrorNormal)} 尺寸={halfW * 2f:F2}×{halfH * 2f:F2}");
    }

    /// <summary>从镜子里那块 “mirror” 材质的面片量出镜面的中心、朝向和大小。</summary>
    bool MeasureMirror()
    {
        var mf = mirrorRoot.GetComponent<MeshFilter>();
        var mr = mirrorRoot.GetComponent<MeshRenderer>();
        if (!mf || !mr || !mf.sharedMesh) return false;

        var mesh = mf.sharedMesh;
        var mats = mr.sharedMaterials;
        int sub = -1;
        for (int i = 0; i < mats.Length; i++)
            if (mats[i] && mats[i].name.ToLowerInvariant().Contains("mirror")) { sub = i; break; }
        if (sub < 0) sub = mesh.subMeshCount - 1;

        var tris = mesh.GetTriangles(sub);
        var verts = mesh.vertices;
        if (tris.Length < 3) return false;

        var idx = new HashSet<int>();
        for (int i = 0; i < tris.Length; i++) idx.Add(tris[i]);

        Vector3 localCenter = Vector3.zero;
        foreach (var i in idx) localCenter += verts[i];
        localCenter /= Mathf.Max(1, idx.Count);

        Vector3 a = mirrorRoot.TransformPoint(verts[tris[0]]);
        Vector3 b = mirrorRoot.TransformPoint(verts[tris[1]]);
        Vector3 c = mirrorRoot.TransformPoint(verts[tris[2]]);
        // 前三个顶点可能是退化三角形，取投影面积最大的那一片。
        Vector3 nrm = Vector3.Cross(b - a, c - a);
        float bestArea = nrm.sqrMagnitude;
        for (int i = 0; i + 2 < tris.Length; i += 3)
        {
            Vector3 p0 = mirrorRoot.TransformPoint(verts[tris[i]]);
            Vector3 p1 = mirrorRoot.TransformPoint(verts[tris[i + 1]]);
            Vector3 p2 = mirrorRoot.TransformPoint(verts[tris[i + 2]]);
            Vector3 cr = Vector3.Cross(p1 - p0, p2 - p0);
            if (cr.sqrMagnitude > bestArea) { bestArea = cr.sqrMagnitude; nrm = cr; }
        }

        mirrorCenter = mirrorRoot.TransformPoint(localCenter);
        if (nrm.sqrMagnitude < 1e-8f)
        {
            Debug.LogWarning("[谜题三] 镜面网格太扁，算不出法线");
            return false;
        }
        nrm.Normalize();

        // 让法线朝向客厅（玩家站的那一边）
        Vector3 viewer = Level2Director.Instance && Level2Director.Instance.Player
            ? Level2Director.Instance.Player.position
            : mirrorCenter + Vector3.right;
        if (Vector3.Dot(nrm, viewer - mirrorCenter) < 0f) nrm = -nrm;
        mirrorNormal = nrm;

        Vector3 R = Vector3.Cross(Vector3.up, mirrorNormal).normalized;
        Vector3 U = Vector3.Cross(mirrorNormal, R).normalized;

        float minR = float.MaxValue, maxR = float.MinValue, minU = float.MaxValue, maxU = float.MinValue;
        foreach (var i in idx)
        {
            Vector3 p = mirrorRoot.TransformPoint(verts[i]);
            float r = Vector3.Dot(p - mirrorCenter, R);
            float u = Vector3.Dot(p - mirrorCenter, U);
            minR = Mathf.Min(minR, r); maxR = Mathf.Max(maxR, r);
            minU = Mathf.Min(minU, u); maxU = Mathf.Max(maxU, u);
        }

        halfW = Mathf.Max(.05f, (maxR - minR) * .5f);
        halfH = Mathf.Max(.05f, (maxU - minU) * .5f);
        mirrorCenter += R * ((minR + maxR) * .5f) + U * ((minU + maxU) * .5f);
        return true;
    }

    void BuildMirrorCamera()
    {
        var go = new GameObject("镜面反射相机");
        mirrorCam = go.AddComponent<Camera>();
        mirrorCam.enabled = false;                  // 只在需要时手动 Render
        mirrorCam.targetTexture = rt;
        mirrorCam.nearClipPlane = .05f;
        mirrorCam.farClipPlane = 50f;
        mirrorCam.clearFlags = CameraClearFlags.SolidColor;
        mirrorCam.backgroundColor = new Color(.04f, .045f, .055f);
        go.transform.SetParent(transform, false);
    }

    void LateUpdate()
    {
        if (!mirrorCam || !disc || !Camera.main) return;
        var viewer = Camera.main.transform;
        if ((viewer.position - mirrorCenter).sqrMagnitude > 400f) return;   // 离得远就不渲染

        Vector3 N = mirrorNormal;
        float side = Vector3.Dot(N, viewer.position - mirrorCenter);
        if (side < .05f) return;                                            // 人不在镜子正面

        // 镜子该显示的是“玩家眼睛看到的那一面”：把玩家相机整个镜过去。
        Vector3 camPos = mirrorCenter + Vector3.Reflect(viewer.position - mirrorCenter, N);

        // 视线别照着镜子的法线走：这面穿衣镜是往前倾的，纯反射轴会把人顶出画面。
        // 直接瞄准玩家本人的包围盒中心 —— 角色的动画姿态会让身体偏离根节点十几到三十厘米，
        // 用根节点当目标会让镜子里的自己一直贴在镜边。
        if (!bodySkin) bodySkin = FindBodySkin();
        Vector3 target = bodySkin ? bodySkin.bounds.center : viewer.position;
        Vector3 aim = new Vector3(target.x, Mathf.Lerp(target.y, viewer.position.y - .30f, .35f), target.z);
        Vector3 dir = aim - camPos;
        if (dir.sqrMagnitude < .001f) return;
        mirrorCam.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(dir, Vector3.up));

        // 框住整块镜面，再往里收一点：镜面窄，人物在镜子里才不至于太小。
        float dist = Mathf.Max(.20f, Vector3.Distance(camPos, mirrorCenter));
        float vfov = 2f * Mathf.Atan(halfH * .84f / dist) * Mathf.Rad2Deg;
        mirrorCam.fieldOfView = Mathf.Clamp(vfov, 25f, 95f);
        // 近裁剪面推到镜面之后：镜框、镜面网格都在这个平面之前，一律不参与反射。
        mirrorCam.nearClipPlane = Mathf.Max(.05f, dist - .04f);

        // 渲染反射时必须把这块镜子自己藏起来，否则镜后相机会先撞上镜面网格的背面。
        var rootRenderer = mirrorRoot ? mirrorRoot.GetComponent<Renderer>() : null;
        if (rootRenderer) rootRenderer.enabled = false;
        if (discRenderer) discRenderer.enabled = false;
        if (adultDisc) adultDisc.gameObject.SetActive(false);

        // 第一人称把头藏了，镜子里只会剩一个没有头的身体。借“只有影子”的那份完整网格，
        // 只在镜像相机渲染的这一瞬把它露出来 —— 主相机永远看不到它。
        if (!bodyShadow) bodyShadow = FindBodyShadow();
        var prevMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        if (bodyShadow)
        {
            prevMode = bodyShadow.shadowCastingMode;
            bodyShadow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        mirrorCam.Render();

        if (bodyShadow) bodyShadow.shadowCastingMode = prevMode;
        if (rootRenderer) rootRenderer.enabled = true;
        if (discRenderer) discRenderer.enabled = true;
        if (adultDisc && adultShown) adultDisc.gameObject.SetActive(true);
    }

    /// <summary>玩家身上那份“完整全身（含头）”的纯阴影渲染器，由 FullBodyShadow 生成。</summary>
    SkinnedMeshRenderer FindBodyShadow()
    {
        var player = Level2Director.Instance ? Level2Director.Instance.Player : null;
        if (!player) return null;
        foreach (var r in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (r && r.gameObject.name == "Full character shadow") return r;
        return null;
    }

    /// <summary>玩家自己的蒙皮网格（不含那份纯阴影副本），用来量他到底站在哪。</summary>
    SkinnedMeshRenderer FindBodySkin()
    {
        var player = Level2Director.Instance ? Level2Director.Instance.Player : null;
        if (!player) return null;
        foreach (var r in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (r && r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) return r;
        return null;
    }

    // ---------------- 桌上的盒子 ----------------

    void BuildBox()
    {
        // 场景里有九个同名 SM_Box_A4，取客厅圆桌上那一个（沙发与电视之间，离坐标最近）。
        Vector3 want = new Vector3(-22.472f, .894f, -98.874f);
        Transform target = null;
        float best = float.MaxValue;
        foreach (var t in L2.FindAll("SM_Box_A4"))
        {
            float d = (t.position - want).sqrMagnitude;
            if (d < best) { best = d; target = t; }
        }
        if (!target || best > 4f)
        {
            Debug.LogWarning("[谜题三] 没找到客厅桌上的盒子 SM_Box_A4");
            return;
        }

        var bounds = L2.BoundsOf(target);
        var host = new GameObject("桌上的盒子·交互");
        host.transform.position = bounds.center;
        var col = host.AddComponent<BoxCollider>();
        col.size = new Vector3(Mathf.Max(.34f, bounds.size.x + .10f),
                               Mathf.Max(.26f, bounds.size.y + .12f),
                               Mathf.Max(.34f, bounds.size.z + .10f));

        box = host.AddComponent<Level2LetterBox>();
        box.host = this;
        box.visual = target;
        box.Bind(target);
        Debug.Log($"[谜题三] 桌上的盒子 {L2.Fmt(target.position)}");
    }

    // ---------------- 问答流程 ----------------

    public void Talk()
    {
        if (!unlocked)
        {
            Ui?.Note("落地镜", "镜子里是一个还没长大的孩子。她好像在等我说点什么。");
            return;
        }
        if (talked) { if (!answered) Ask(); return; }
        talked = true;

        Ui?.Say("内心独白", "原来我一直在和过去的自己对视。");
        // 直接入队排在独白后面，不靠 Invoke 定时（Invoke 一旦碰上组件被禁用 / 暂停就会静默丢掉，
        // 表现就是“独白看完、问答没出来”）。队列本身保证顺序，所以这里同步调用是安全的。
        Ask();
    }

    void Ask()
    {
        Debug.Log("[谜题三] 弹出问答页（两选一）");
        Ui?.Ask("镜子", "你现在开心吗，有什么想对此时小时候的你说？",
            new[]
            {
                "我没能一直开心，但我好好走到现在了",
                "谢谢你当年的满怀期待，让我来到了这里",
            },
            OnAnswer);
    }

    public void Answer(int index) => OnAnswer(index);

    void OnAnswer(int index)
    {
        if (answered) return;
        answered = true;
        Debug.Log("[谜题三] 玩家回答：" + index);

        Ui?.Say("镜子里", index == 0
            ? "镜子里的孩子没有哭。她只是安静地看着我，像在确认我有没有骗她。"
            : "镜子里的孩子笑了一下。那个笑我很熟悉 —— 我很久没有这样笑过了。");
        Ui?.Note("桌上的盒子", "旁边桌上那个盒子，自己弹开了一条缝。");

        if (box) box.Open();
    }

    // ---------------- 信 ----------------

    public void OnBoxOpened()
    {
        if (letterOpened) return;
        letterOpened = true;

        // 信出现的那一刻，镜子里的人长大了。
        ShowAdult();

        Ui?.Letter("给小时候的我",
            "恭喜通过。\n\n" +
            "这封信不是小时候写给未来的，\n" +
            "是现在的我，写给那时候的你。\n\n" +
            "—— 长大后的你",
            OnLetterClosed);
    }

    void ShowAdult()
    {
        if (!adultDisc) return;
        adultShown = true;
        adultDisc.gameObject.SetActive(true);
        if (!adultMaterial) return;
        StopAllCoroutines();
        StartCoroutine(FadeAdult());
    }

    System.Collections.IEnumerator FadeAdult()
    {
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * .55f;
            var c = new Color(1f, 1f, 1f, Mathf.Clamp01(t));
            adultMaterial.SetColor("_BaseColor", c);
            if (adultMaterial.HasProperty("_Color")) adultMaterial.SetColor("_Color", c);
            yield return null;
        }
    }

    void OnLetterClosed()
    {
        if (finished) return;
        finished = true;
        foreach (var l in FindObjectsOfType<L2Flicker>()) l.gameObject.SetActive(false);

        // 黑幕白字：一个字一个字打出来，全部出完后才允许按空格。
        Ui?.Ending("主角内心独白", new[]
        {
            "原来当年的信，不是用来等待一个完美的未来。",
            "它是留给我的一场重逢，",
            "让长大的我，终于可以好好回应那个满怀期待的小孩。",
        }, OnEndingDone);
    }

    void OnEndingDone()
    {
        Debug.Log("[谜题三] 全部解密闭环完成");
        Ui?.Ending("—— 完 ——", new[] { "" }, null);
    }

    // ---------------- 存档 ----------------

    /// <summary>0 未解锁 / 1 已解锁 / 2 已答完 / 3 已读信 / 4 已看完结局。</summary>
    public int Stage
    {
        get
        {
            if (finished) return 4;
            if (letterOpened) return 3;
            if (answered) return 2;
            if (unlocked) return 1;
            return 0;
        }
    }

    public void RestoreStage(int stage)
    {
        if (stage <= 0) return;
        unlocked = true;
        if (stage >= 2)
        {
            talked = true;
            answered = true;
            if (box) box.Open();
        }
        if (stage >= 3)
        {
            letterOpened = true;
            ShowAdult();
        }
        if (stage >= 4) finished = true;
        Debug.Log($"[谜题三] 读档恢复：进度 {stage}");
    }
}

/// <summary>镜子前的那一下就按 E。</summary>
public sealed class Level2MirrorTalk : L2Interactable
{
    public Level2Mirror host;

    public override string Prompt
    {
        get
        {
            if (!host) return "";
            if (!host.Unlocked) return "E 看看镜子";
            if (host.Answered) return "E 再看一眼镜子";
            return "E 站到镜子前";
        }
    }

    public override void Interact(PlayerInventory inventory) => host?.Talk();
}

/// <summary>客厅桌上那个小盒子。问答之后自己弹开，再按 E 打开读信。</summary>
public sealed class Level2LetterBox : L2Interactable
{
    public Level2Mirror host;
    public Transform visual;
    bool popped, read;
    Vector3 restPos;
    float popT;

    public bool IsPopped => popped;

    public void Bind(Transform box)
    {
        visual = box;
        restPos = box.position;
    }

    public void Open()
    {
        if (popped) return;
        popped = true;
        popT = 0f;
        Debug.Log("[谜题三] 盒子自动弹开");
    }

    public override string Prompt
    {
        get
        {
            if (!popped) return "一个旧木盒";
            if (read) return "信已经读过了";
            return "E 打开盒子";
        }
    }

    public override void Interact(PlayerInventory inventory)
    {
        if (!popped) return;
        if (read) return;
        read = true;
        host?.OnBoxOpened();
    }

    void Update()
    {
        if (!popped || popT >= 1f) return;
        // 弹开：先轻轻跳一下，再落回去 —— 没有单独的盖子网格，用整体位移表达“开了”。
        popT = Mathf.Min(1f, popT + Time.deltaTime * 2.2f);
        float bounce = Mathf.Sin(popT * Mathf.PI) * .06f;
        if (visual) visual.position = restPos + Vector3.up * bounce;
    }
}
