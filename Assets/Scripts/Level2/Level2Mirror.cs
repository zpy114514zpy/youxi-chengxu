using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 谜题三 · 闭环问答（客厅）。
/// 客厅中间立着一面落地镜：镜面是真反射（镜像相机 + RenderTexture），
/// 玩家站在镜子前能看到自己 —— 一个还没长大的自己。
/// 镜前问答选完 → 桌上那个盒子自己弹开 → 打开盒子读信 → 黑幕白字收束。
/// </summary>
public sealed class Level2Mirror : MonoBehaviour
{
    Level2Ui Ui => Level2Director.Instance ? Level2Director.Instance.Ui : null;

    Transform mirrorPlane, adultLayer;
    Renderer planeRenderer;
    Camera mirrorCam;
    RenderTexture rt;
    Material mirrorMaterial, adultMaterial;
    Level2MirrorTalk talk;
    Level2LetterBox box;

    bool unlocked, talked, answered, letterOpened, finished;
    public bool Unlocked => unlocked;
    public bool Answered => answered;

    // 客厅中央：沙发在西、电视在东，镜子立在中间偏东，正面朝西（朝沙发那边）。
    static readonly Vector3 MirrorPos = new Vector3(-19.25f, 0f, -97.95f);
    const float MirrorYaw = 90f;      // 面朝 -X

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

    // ---------------- 落地镜 ----------------

    void BuildMirror()
    {
        // 先落地：从上方打一条射线找一楼地面（脚踩的 0.60）。
        float floorY = .6f;
        if (Physics.Raycast(new Vector3(MirrorPos.x, 2.4f, MirrorPos.z), Vector3.down, out var hit, 3f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            floorY = hit.point.y;

        var root = new GameObject("落地镜");
        root.transform.SetPositionAndRotation(new Vector3(MirrorPos.x, floorY, MirrorPos.z),
            Quaternion.Euler(0f, MirrorYaw, 0f));

        var wood = L2.Unlit(new Color(.20f, .15f, .11f));
        var woodDark = L2.Unlit(new Color(.12f, .09f, .07f));

        // 外框：两根立柱 + 上下横梁 + 底座，拼一面旧木框镜子。
        L2.Box("镜框·左", root.transform, Local(root.transform, -.50f, 1.02f, 0f), new Vector3(.08f, 2.04f, .10f), wood, root.transform.rotation);
        L2.Box("镜框·右", root.transform, Local(root.transform, .50f, 1.02f, 0f), new Vector3(.08f, 2.04f, .10f), wood, root.transform.rotation);
        L2.Box("镜框·上", root.transform, Local(root.transform, 0f, 2.02f, 0f), new Vector3(1.08f, .10f, .10f), wood, root.transform.rotation);
        L2.Box("镜框·下", root.transform, Local(root.transform, 0f, .06f, 0f), new Vector3(1.08f, .12f, .10f), wood, root.transform.rotation);
        L2.Box("镜座", root.transform, Local(root.transform, 0f, .03f, 0f), new Vector3(1.20f, .06f, .42f), woodDark, root.transform.rotation);

        // 镜面：一张离框面 1cm 的薄片，材质挂 RenderTexture。
        rt = new RenderTexture(768, 1024, 24, RenderTextureFormat.Default) { hideFlags = HideFlags.DontSave };
        rt.Create();

        mirrorMaterial = L2.Unlit(Color.white);
        mirrorMaterial.SetTexture("_BaseMap", rt);
        FlipV(mirrorMaterial);
        if (mirrorMaterial.HasProperty("_Cull")) mirrorMaterial.SetFloat("_Cull", 0f);

        mirrorPlane = L2.Box("镜面", root.transform, Local(root.transform, 0f, 1.02f, -.045f),
            new Vector3(.92f, 1.88f, .012f), mirrorMaterial, root.transform.rotation);
        planeRenderer = mirrorPlane.GetComponent<Renderer>();
        planeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // 镜子里“长大之后”的那个身影：一层贴在镜面前的剪影，默认全透明。
        adultMaterial = L2.Unlit(new Color(1f, 1f, 1f, 0f), transparent: true);
        adultLayer = L2.Box("镜中人影", root.transform, Local(root.transform, 0f, 1.02f, -.056f),
            new Vector3(.78f, 1.62f, .004f), adultMaterial, root.transform.rotation);
        adultLayer.gameObject.SetActive(false);
        if (Level2Ui.Instance && Level2Ui.Instance.AdultFigure)
            adultMaterial.SetTexture("_BaseMap", Level2Ui.Instance.AdultFigure);
        FlipV(adultMaterial);

        // 镜子的碰撞：人不能穿过去。
        var solid = root.AddComponent<BoxCollider>();
        solid.center = new Vector3(0f, 1.02f, 0f);
        solid.size = new Vector3(1.10f, 2.06f, .16f);

        // 交互盒：比镜面稍大，站在镜子前按 E 就能触发。
        var host = new GameObject("落地镜·交互");
        host.transform.SetPositionAndRotation(Local(root.transform, 0f, 1.10f, -.30f), Quaternion.Euler(0f, MirrorYaw, 0f));
        var box = host.AddComponent<BoxCollider>();
        box.size = new Vector3(1.10f, 2.0f, .60f);

        talk = host.AddComponent<Level2MirrorTalk>();
        talk.host = this;

        // 客厅是整间屋子情绪的落点：给镜前一小片柔和暖光，别让这里冷得像走廊尽头。
        var glow = new GameObject("客厅暖光");
        glow.transform.SetParent(root.transform, false);
        glow.transform.position = Local(root.transform, -.55f, 1.85f, -.65f);
        var lamp = glow.AddComponent<Light>();
        lamp.type = LightType.Point;
        lamp.color = new Color(1f, .82f, .60f);
        lamp.range = 3.4f;
        lamp.intensity = .55f;
        lamp.shadows = LightShadows.Soft;

        BuildMirrorCamera();
        Debug.Log($"[谜题三] 落地镜已立在 {L2.Fmt(root.transform.position)}（地面 y={floorY:F2}）");
    }

    static Vector3 Local(Transform root, float x, float y, float z) => root.TransformPoint(new Vector3(x, y, z));

    /// <summary>
    /// Cube 原语的正面 UV 在这块竖着的镜片上是上下颠倒的（RenderTexture 和剪影贴图都跟着翻了）。
    /// 统一把贴图垂直翻一次，比改网格便宜，也不影响别的物体。
    /// </summary>
    static void FlipV(Material m)
    {
        if (!m) return;
        m.SetTextureScale("_BaseMap", new Vector2(1f, -1f));
        m.SetTextureOffset("_BaseMap", new Vector2(0f, 1f));
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

    void BuildMirrorCamera()
    {
        var go = new GameObject("镜面反射相机");
        mirrorCam = go.AddComponent<Camera>();
        mirrorCam.enabled = false;                  // 只在需要时手动 Render
        mirrorCam.targetTexture = rt;
        mirrorCam.fieldOfView = 68f;
        mirrorCam.nearClipPlane = .05f;
        mirrorCam.farClipPlane = 60f;
        mirrorCam.clearFlags = CameraClearFlags.SolidColor;
        mirrorCam.backgroundColor = new Color(.05f, .05f, .07f);

        // 镜像相机里的画面左右是反的，这正是镜子的效果；不用再翻。
        go.transform.SetParent(transform, false);
    }

    void LateUpdate()
    {
        if (!mirrorCam || !mirrorPlane || !Camera.main) return;
        var viewer = Camera.main.transform.position;
        if ((viewer - mirrorPlane.position).sqrMagnitude > 220f) return;   // 离得远就不渲染

        Vector3 M = mirrorPlane.position;
        Vector3 N = mirrorPlane.right;                 // 薄片的 +X 就是镜面法线
        if (Vector3.Dot(N, viewer - M) < 0f) N = -N;

        Vector3 camPos = M + Vector3.Reflect(viewer - M, N);
        Vector3 dir = Vector3.Reflect((M - viewer).normalized, N);
        if (dir.sqrMagnitude < .001f) return;

        mirrorCam.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(dir, Vector3.up));
        mirrorCam.fieldOfView = Camera.main.fieldOfView;

        if (planeRenderer) planeRenderer.enabled = false;
        mirrorCam.Render();
        if (planeRenderer) planeRenderer.enabled = true;
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
        Invoke(nameof(Ask), .1f);
    }

    void Ask()
    {
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
        if (!adultLayer) return;
        adultLayer.gameObject.SetActive(true);
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

        // 黑幕白字：一句一句浮出来，全部出完后才允许按空格。
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
