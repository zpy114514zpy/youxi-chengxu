using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 密码箱（SM_Box_A15）：装在房间高处的架子上，玩家个子不够，必须把凳子推到箱子下面、
/// 踩上凳子才够得到。交互后弹出 8 位数字键盘，输入 20221005 打开箱子，
/// 信封碎片③出现在箱子里，玩家拾取后触发“第三个任务完成”。
/// </summary>
[DisallowMultipleComponent]
public sealed class PasswordBoxPuzzle : MonoBehaviour
{
    [Header("答案")]
    public string correctCode = "20221005";

    [Header("信封碎片")]
    public string fragmentName = "信封碎片②";
    [TextArea] public string fragmentText = "我想和爸爸妈妈一直在一起";
    public string fragmentDisplayName = "信封碎片②";
    [Tooltip("箱子里那块拼图碎片的模型名（场景里 SM_Box_A15 内部的 Plane002）。")]
    public string fragmentModelName = "Plane002";

    [Header("距离")]
    [Tooltip("凳子离箱子的水平距离小于这个值，才算“把凳子推到箱子下面”。")]
    public float stoolRadius = 1.6f;
    [Tooltip("玩家走到这个范围内、又还够不到时，提示去找凳子。")]
    public float approachRadius = 3.2f;
    [Tooltip("房间介绍播完之后，还要在箱子下站这么久才提示找凳子。")]
    public float hintDelay = .8f;

    bool typing, unlocked, hintedStool, hintedCalendar, fragmentReady;
    float hintDwell;
    DoorRoomIntro roomDoor;
    string input = "";
    float errorUntil;
    GameObject fragment;
    Vector3 fragmentHome;
    GameObject lidObject;
    Vector3 lidHinge, lidRestPos;
    Quaternion lidRestRot;
    Vector3 lidAxis;
    bool lidBuilt;
    PlayerController player;
    Font font;

    public bool Unlocked => unlocked;
    public string Prompt
    {
        get
        {
            if (unlocked) return "";
            return CanReach() ? "E 输入密码" : "太高了，够不到";
        }
    }

    void Awake()
    {
        font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 26);
        // 碎片本来就在箱子内部（Plane002），先藏起来，等密码正确、盖子打开后再让它升出来。
        fragment = FindFragmentModel();
        if (fragment) fragment.SetActive(false);
    }

    void OnDestroy()
    {
        UiInputBlocker.Active = false;
        if (font) Destroy(font);
    }

    // ---------- 交互 ----------

    public void Interact()
    {
        if (unlocked || typing) return;
        if (!CanReach())
        {
            // 站在地上够不到：给出方向性提示，不让玩家干瞪眼。
            StoryProgress.Instance?.EnqueueCenter("够不到", "箱子太高了，我得找个凳子垫脚。");
            return;
        }
        OpenKeypad();
    }

    /// <summary>凳子推到箱子下面 + 玩家踩在凳子上，才算够得到。</summary>
    bool CanReach()
    {
        if (!player) player = FindObjectOfType<PlayerController>();
        if (!player) return false;
        foreach (var stool in StoolStep.All)
        {
            if (!stool || !stool.PlayerOnTop) continue;
            if (stool.IsNear(transform.position, stoolRadius)) return true;
        }
        return false;
    }

    void Update()
    {
        if (!player) player = FindObjectOfType<PlayerController>();
        if (typing) { HandleTyping(); return; }

        if (!unlocked && !hintedStool && player) TickStoolHint();
    }

    /// <summary>
    /// “或许我需要找一个凳子”的提示：要等房间介绍（开门那页）播完、玩家还站在箱子下面，
    /// 再停留一小会儿才弹，避免一进门两条 UI 挤在一起。
    /// </summary>
    void TickStoolHint()
    {
        if (!NeedStool()) { hintDwell = 0f; return; }
        if (StoryProgress.Instance && StoryProgress.Instance.IsBusy) { hintDwell = 0f; return; }

        hintDwell += Time.deltaTime;
        // 正常：房间介绍播完后再等 0.8 秒；门没配介绍（或玩家没走开门流程）时兜底等 6.8 秒，
        // 保证提示不会永远不出现。
        if (hintDwell < (RoomIntroduced ? hintDelay : hintDelay + 6f)) return;
        hintedStool = true;
        StoryProgress.Instance?.EnqueueCenter("提示", "或许我需要找一个凳子。");
    }

    bool NeedStool()
    {
        Vector3 a = player.transform.position, b = transform.position;
        a.y = b.y = 0f;
        // 走到箱子下方、但还站在地上、又够不到：需要凳子。
        return Vector3.Distance(a, b) < approachRadius
            && player.transform.position.y < transform.position.y - .2f
            && !CanReach();
    }

    /// <summary>这间屋子的门有没有已经播过“房间介绍”。场景里没有配介绍的门就当作已介绍，不会卡住提示。</summary>
    bool RoomIntroduced
    {
        get
        {
            if (!roomDoor) roomDoor = FindRoomDoor();
            return !roomDoor || roomDoor.HasIntroduced;
        }
    }

    DoorRoomIntro FindRoomDoor()
    {
        DoorRoomIntro best = null;
        float bestDistance = 8f;
        foreach (var door in FindObjectsOfType<DoorRoomIntro>())
        {
            float distance = Vector3.Distance(door.transform.position, transform.position);
            if (distance < bestDistance) { bestDistance = distance; best = door; }
        }
        return best;
    }

    // ---------- 密码键盘 ----------

    void OpenKeypad()
    {
        typing = true;
        input = "";
        errorUntil = 0f;
        UiInputBlocker.Active = true;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        Debug.Log("[密码箱] 打开键盘", this);
    }

    void CloseKeypad()
    {
        typing = false;
        UiInputBlocker.Active = false;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // 第一次交互又退出来：提示去看日历（只在还没开箱时给一次）。
        if (!unlocked && !hintedCalendar)
        {
            hintedCalendar = true;
            StoryProgress.Instance?.EnqueueCenter("线索", "每年我都会把自己的生日日期画在日历上。");
        }
    }

    void HandleTyping()
    {
        if (Input.GetKeyDown(KeyCode.Escape)) { CloseKeypad(); return; }
        if (Input.GetKeyDown(KeyCode.Backspace) && input.Length > 0)
        {
            input = input.Substring(0, input.Length - 1);
            return;
        }
        for (int digit = 0; digit <= 9; digit++)
        {
            var main = (KeyCode)((int)KeyCode.Alpha0 + digit);
            var pad = (KeyCode)((int)KeyCode.Keypad0 + digit);
            if (Input.GetKeyDown(main) || Input.GetKeyDown(pad))
            {
                if (input.Length < 8) input += digit.ToString();
                return;
            }
        }
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Submit();
    }

    void Submit()
    {
        if (input == correctCode)
        {
            CloseKeypad();
            Unlock();
        }
        else
        {
            errorUntil = Time.time + 2f;
            input = "";
        }
    }

    // ---------- 开箱 ----------

    void Unlock()
    {
        unlocked = true;
        StoryProgress.Instance?.RecordClue("密码箱密码正确：20221005。");
        StartCoroutine(OpenRoutine());
        Debug.Log("[密码箱] 密码正确，箱子打开", this);
    }

    IEnumerator OpenRoutine()
    {
        Bounds box = BoxBounds();

        // 1) 盖子自动弹开（箱子模型是一整块，这里另做一块薄盖板，绕后侧顶边掀起来）。
        BuildLid(box);
        float t = 0f;
        const float lidDuration = .55f;
        while (t < lidDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / lidDuration));
            SetLidAngle(16f * k);
            yield return null;
        }
        SetLidAngle(16f);

        // 2) 箱子里的拼图碎片（Plane002）从箱口飘出来，等玩家按 E 拾取。
        if (!fragment) fragment = FindFragmentModel();
        if (!fragment)
        {
            Debug.LogWarning("[密码箱] 箱子里没有找到碎片模型 " + fragmentModelName + "。", this);
            yield break;
        }
        fragment.SetActive(true);
        SetupFragment();

        Vector3 toward = TowardPlayer(box);
        Bounds now = BoxBounds();
        // 箱子正上方就是隔板，碎片在原地升起会被挡住；让它从箱口前方飘出来、停在玩家眼前。
        float halfDepth = Mathf.Abs(toward.x) * now.extents.x + Mathf.Abs(toward.z) * now.extents.z;
        Vector3 start = new Vector3(now.center.x, now.max.y - .03f, now.center.z) + toward * (halfDepth + .02f);
        Vector3 startScale = fragment.transform.localScale;
        fragmentHome = new Vector3(now.center.x, now.max.y + .02f, now.center.z) + toward * .19f;
        fragment.transform.position = start;

        float rise = 0f;
        const float riseDuration = .5f;
        while (rise < riseDuration)
        {
            rise += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(rise / riseDuration));
            if (!fragment) yield break;
            fragment.transform.position = Vector3.Lerp(start, fragmentHome, k);
            fragment.transform.localScale = startScale * Mathf.Lerp(.35f, 1.5f, k);
            yield return null;
        }
        if (!fragment) yield break;
        fragment.transform.localScale = startScale * 1.5f;
        fragment.transform.position = fragmentHome;
        fragmentReady = true;

        Debug.Log("[密码箱] 盖子已打开，碎片 " + fragment.name + " 位于 " + fragmentHome.ToString("F3"), this);
    }

    /// <summary>
    /// 箱口正上方只有 9 厘米净空（衣柜隔板）、下面又是层板，盖子的活动余地很小。
    /// 这里全部用世界坐标算：盖板先盖在箱口上，再绕后侧顶边那条轴旋转（最多 16°，前缘抬起 8.8 厘米）。
    /// </summary>
    void BuildLid(Bounds box)
    {
        if (lidBuilt) return;
        lidBuilt = true;

        Vector3 toward = TowardPlayer(box);
        Vector3 right = Vector3.Cross(Vector3.up, toward).normalized;
        float halfDepth = Mathf.Abs(toward.x) * box.extents.x + Mathf.Abs(toward.z) * box.extents.z;
        float halfWidth = Mathf.Abs(right.x) * box.extents.x + Mathf.Abs(right.z) * box.extents.z;

        var lid = GameObject.CreatePrimitive(PrimitiveType.Cube);
        lid.name = "密码箱盖子";
        var lidCollider = lid.GetComponent<Collider>();
        if (lidCollider) Destroy(lidCollider);

        // 盖好时的世界位置：箱口正中、贴着箱顶；尺寸正好盖住箱口。
        lidRestPos = new Vector3(box.center.x, box.max.y + .008f, box.center.z);
        lidRestRot = Quaternion.LookRotation(toward, Vector3.up);
        lid.transform.SetPositionAndRotation(lidRestPos, lidRestRot);
        lid.transform.localScale = new Vector3(halfWidth * 2f, .012f, halfDepth * 2f);

        var source = GetComponentInChildren<MeshRenderer>();
        if (source && source.sharedMaterial) lid.GetComponent<MeshRenderer>().sharedMaterial = source.sharedMaterial;

        lidObject = lid;
        // 铰轴：后侧顶边中点；旋转轴是水平方向里和 toward 垂直的那个轴。
        lidHinge = new Vector3(box.center.x, box.max.y, box.center.z) - toward * halfDepth;
        lidAxis = right;
    }

    /// <summary>把盖子绕后侧顶边转到 angle 度（0=盖好，正值=前缘抬起）。</summary>
    void SetLidAngle(float angle)
    {
        if (!lidObject) return;
        Quaternion spin = Quaternion.AngleAxis(-angle, lidAxis);
        lidObject.transform.position = lidHinge + spin * (lidRestPos - lidHinge);
        lidObject.transform.rotation = spin * lidRestRot;
    }

    /// <summary>
    /// 场景里的 Plane002 / 对象007 都是空网格（只有包围盒、没有三角面），直接显示什么都看不见。
    /// 这里用同一个信封材质铺一张小纸片，放在碎片轴心上，保证玩家看得到、也瞄得准。
    /// </summary>
    Renderer BuildPaper()
    {
        var existing = fragment.transform.Find("碎片纸片");
        if (existing) return existing.GetComponent<Renderer>();

        Material material = null;
        foreach (var r in fragment.GetComponentsInChildren<Renderer>(true))
        {
            if (r.sharedMaterial) material = r.sharedMaterial;
            r.enabled = false;                       // 空网格不参与渲染
        }

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "碎片纸片";
        var quadCollider = quad.GetComponent<Collider>();
        if (quadCollider) Destroy(quadCollider);
        quad.transform.position = fragment.transform.position;
        // 玩家是踩在凳子上、几乎平视箱口的，纸片平放只会变成一条线；这里让它竖起来正对玩家。
        // Quad 的正面朝 -Z，所以让 +Z 背对玩家，正面才朝向玩家、字不会左右反。
        Vector3 toward = TowardPlayer(BoxBounds());
        quad.transform.rotation = Quaternion.LookRotation(-(toward + Vector3.up * .28f).normalized, Vector3.up);
        Vector3 lossy = fragment.transform.lossyScale;
        quad.transform.localScale = new Vector3(
            .10f / Mathf.Max(.0001f, Mathf.Abs(lossy.x)),
            .10f / Mathf.Max(.0001f, Mathf.Abs(lossy.y)),
            1f / Mathf.Max(.0001f, Mathf.Abs(lossy.z)));
        quad.transform.SetParent(fragment.transform, true);

        var paperRenderer = quad.GetComponent<MeshRenderer>();
        if (material) paperRenderer.sharedMaterial = material;
        var instance = paperRenderer.material;
        if (instance.HasProperty("_Cull")) instance.SetFloat("_Cull", 0f);   // 双面
        return paperRenderer;
    }

    Vector3 TowardPlayer(Bounds box)
    {
        if (!player) player = FindObjectOfType<PlayerController>();
        Vector3 target = player ? player.transform.position
            : (Camera.main ? Camera.main.transform.position : transform.position + transform.forward);
        Vector3 toward = Vector3.ProjectOnPlane(target - box.center, Vector3.up);
        if (toward.sqrMagnitude < .0004f) toward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (toward.sqrMagnitude < .0004f) toward = Vector3.forward;
        return toward.normalized;
    }

    GameObject FindFragmentModel()
    {
        var scene = gameObject.scene;
        if (!scene.IsValid()) return null;
        Transform best = null;
        float bestDistance = 1.5f;
        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != fragmentModelName) continue;
                float distance = (t.position - transform.position).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = t;
            }
        return best ? best.gameObject : null;
    }

    /// <summary>把场景里原本藏在箱子里的碎片挂上拾取组件、碰撞体和描边。</summary>
    void SetupFragment()
    {
        var envelope = fragment.GetComponent<EnvelopeFragment>();
        if (!envelope) envelope = fragment.AddComponent<EnvelopeFragment>();
        envelope.fragmentName = fragmentName;
        envelope.text = fragmentText;

        var pickup = fragment.GetComponent<PickupItem>();
        if (!pickup) pickup = fragment.AddComponent<PickupItem>();
        pickup.displayName = fragmentDisplayName;
        pickup.storyId = "letter_fragment_2";
        pickup.itemType = "信封碎片";
        pickup.description = fragmentText;

        BuildPaper();

        if (!fragment.GetComponent<Collider>())
        {
            var renderer = fragment.GetComponentInChildren<Renderer>();
            Vector3 world = renderer ? renderer.bounds.size : Vector3.one * .1f;
            Vector3 scale = fragment.transform.lossyScale;
            var box = fragment.AddComponent<BoxCollider>();
            // 碎片很薄很小，拾取盒做成一个 0.22m 见方、0.10m 高的方盒，免得玩家明明瞄准了却打不中。
            float flat = Mathf.Max(.22f, Mathf.Max(world.x, world.z));
            box.size = new Vector3(
                flat / Mathf.Max(.0001f, Mathf.Abs(scale.x)),
                Mathf.Max(.10f, world.y) / Mathf.Max(.0001f, Mathf.Abs(scale.y)),
                flat / Mathf.Max(.0001f, Mathf.Abs(scale.z)));
            box.center = renderer ? fragment.transform.InverseTransformPoint(renderer.bounds.center) : Vector3.zero;
        }

        // 碎片是一张极薄的纸片，描边壳会整片盖住纸面，这里不加描边，靠纸本身的材质。
    }

    void LateUpdate()
    {
        if (!fragmentReady || !fragment || !fragment.activeSelf) return;
        // 轻微浮动，方便玩家在箱口注意到它。
        float bob = Mathf.Sin(Time.time * 2.2f) * .012f;
        fragment.transform.position = fragmentHome + Vector3.up * bob;
    }

    Bounds BoxBounds()
    {
        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            var collider = GetComponent<Collider>();
            return collider ? collider.bounds : new Bounds(transform.position, Vector3.one * .2f);
        }
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    // ---------- 键盘绘制 ----------

    void OnGUI()
    {
        if (!typing) return;
        var old = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, .55f);
        GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = old;

        float width = Mathf.Min(660f, Screen.width * .92f);
        float height = Mathf.Min(340f, Screen.height * .6f);
        var panel = new Rect((Screen.width - width) * .5f, (Screen.height - height) * .5f, width, height);
        Tint(panel, new Color(.05f, .09f, .13f, .96f));
        var edge = new Color(.85f, .74f, .45f, .9f);
        Tint(new Rect(panel.x, panel.y, panel.width, 2f), edge);
        Tint(new Rect(panel.x, panel.yMax - 2f, panel.width, 2f), edge);
        Tint(new Rect(panel.x, panel.y, 2f, panel.height), edge);
        Tint(new Rect(panel.xMax - 2f, panel.y, 2f, panel.height), edge);

        GUI.Label(new Rect(panel.x, panel.y + 22f, width, 46f), "请输入8位数密码", Style(30, TextAnchor.MiddleCenter, new Color(.93f, .90f, .80f)));

        const float slot = 54f, gap = 8f;
        float total = slot * 8f + gap * 7f;
        float startX = panel.x + (width - total) * .5f;
        float slotY = panel.y + 92f;
        for (int i = 0; i < 8; i++)
        {
            var cell = new Rect(startX + i * (slot + gap), slotY, slot, 62f);
            Tint(cell, new Color(.02f, .05f, .08f, .95f));
            var line = new Color(.55f, .80f, .95f, .55f);
            Tint(new Rect(cell.x, cell.yMax - 2f, cell.width, 2f), line);
            if (i < input.Length)
                GUI.Label(cell, input[i].ToString(), Style(32, TextAnchor.MiddleCenter, Color.white));
        }

        if (Time.time < errorUntil)
            GUI.Label(new Rect(panel.x, panel.y + 168f, width, 34f), "密码错误，请再试一次", Style(22, TextAnchor.MiddleCenter, new Color(1f, .45f, .4f)));

        GUI.Label(new Rect(panel.x, panel.yMax - 58f, width, 28f),
            "数字键输入 · Backspace 删除 · Enter 确认 · Esc 取消", Style(18, TextAnchor.MiddleCenter, new Color(.72f, .80f, .86f)));
    }

    void Tint(Rect rect, Color color)
    {
        var old = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = old;
    }

    GUIStyle Style(int size, TextAnchor anchor, Color color)
    {
        var style = new GUIStyle(GUI.skin.label) { font = font, fontSize = size, alignment = anchor, wordWrap = true };
        style.normal.textColor = color;
        return style;
    }
}
