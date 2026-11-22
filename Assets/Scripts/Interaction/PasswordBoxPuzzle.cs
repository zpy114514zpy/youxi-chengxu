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
    public string fragmentName = "信封碎片③";
    [TextArea] public string fragmentText = "希望我以后的每个生日爸爸妈妈都会陪着我，一起幸福的度过！";
    public string fragmentDisplayName = "信封碎片③";
    [Tooltip("箱子里那块拼图碎片的模型名（场景里 SM_Box_A15 内部的 Plane002）。")]
    public string fragmentModelName = "Plane002";

    [Header("距离")]
    [Tooltip("凳子离箱子的水平距离小于这个值，才算“把凳子推到箱子下面”。")]
    public float stoolRadius = 1.6f;

    bool typing, unlocked, hintedCalendar;
    string input = "";
    float errorUntil;
    GameObject fragment;
    Vector3 fragmentHome;
    bool lidHidden;
    bool topRemoved;
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
        // 提示不再自己弹：玩家在箱子下面按 E、又够不到时，Interact() 里才给那一条“找凳子”。
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
        // 箱子已经打开，青色描边高亮会盖住箱口，直接关掉。
        foreach (var outline in GetComponentsInChildren<ProximityOutline>(true)) outline.enabled = false;
        StoryProgress.Instance?.RecordClue("密码箱密码正确：20221005。");
        StartCoroutine(OpenRoutine());
        Debug.Log("[密码箱] 密码正确，箱子打开", this);
    }

    IEnumerator OpenRoutine()
    {
        Bounds box = BoxBounds();

        // 1) 把箱子的顶面从网格里删掉：玩家看到的就是一个真正开了口、里面空着的箱子。
        //    （不做盖板、也不铺任何遮挡物。）
        RemoveTopFace(box);
        // 兼容旧版本：如果场景里残留了一块叫“密码箱盖子”的板子，一并清掉。
        HideLid();

        // 2) 碎片（Plane002）本来就在箱子里，让它平躺在箱口内侧，等玩家对准按 E 拾取。
        if (!fragment) fragment = FindFragmentModel();
        if (!fragment)
        {
            Debug.LogWarning("[密码箱] 箱子里没有找到碎片模型 " + fragmentModelName + "。", this);
            yield break;
        }
        fragment.SetActive(true);

        // 模型原尺寸只有 9cm 见方，摆在 32cm 的箱子里就是一小块白点，玩家根本认不出是信封碎片。
        // 放大到 1.6 倍（约 15cm），打开箱子一眼就能看到纸面上的字。
        var originalScale = fragment.transform.localScale;
        fragment.transform.localScale = originalScale * 1.6f;

        SetupFragment();

        // 碎片轴心不在纸片中心（模型自带偏移），按纸面包围盒把纸片摆到箱口。
        // 箱子塞在矮柜格里，上沿几乎贴着隔板，从外面只能从一条缝看进去：
        // 压在箱底的纸片只剩一条白边，玩家根本认不出是信封。
        // 所以把它挪到箱口靠玩家这一侧、翘起 20°，纸片露在盒口，低头就能看清纸上的字。
        var yawRotation = Quaternion.Euler(0f, fragment.transform.eulerAngles.y, 0f);
        fragment.transform.rotation = Quaternion.Euler(0f, 0f, -20f) * yawRotation;
        Vector3 target = new Vector3(box.center.x - .055f, box.max.y - .012f, box.center.z);
        var paperRenderer = fragment.GetComponentInChildren<Renderer>();
        if (paperRenderer) target += fragment.transform.position - paperRenderer.bounds.center;
        fragmentHome = target;
        fragment.transform.position = target;

        Debug.Log("[密码箱] 顶面已删除，碎片躺在箱口 " + fragmentHome.ToString("F3"), this);
    }

    /// <summary>
    /// 直接改网格：把箱顶那一片三角面剔掉。箱子是单材质闭合模型，
    /// 删掉顶面后就真的“敞口”了，不需要任何额外的板子或暗面。
    /// </summary>
    void RemoveTopFace(Bounds box)
    {
        if (topRemoved) return;
        var filter = GetComponentInChildren<MeshFilter>();
        if (!filter || !filter.sharedMesh)
        {
            Debug.LogWarning("[密码箱] 没有找到箱子网格，无法删除顶面。", this);
            return;
        }
        var mesh = filter.sharedMesh;
        if (!mesh.isReadable)
        {
            Debug.LogWarning("[密码箱] 箱子网格不可读（Read/Write 关闭），无法删除顶面。", this);
            return;
        }
        topRemoved = true;

        var verts = mesh.vertices;
        var tris = mesh.triangles;
        var keep = new List<int>(tris.Length);
        Transform origin = filter.transform;
        float cut = box.max.y - .012f;

        for (int i = 0; i + 2 < tris.Length; i += 3)
        {
            Vector3 centroid = (verts[tris[i]] + verts[tris[i + 1]] + verts[tris[i + 2]]) / 3f;
            if (origin.TransformPoint(centroid).y > cut) continue;     // 顶面整片丢掉
            keep.Add(tris[i]);
            keep.Add(tris[i + 1]);
            keep.Add(tris[i + 2]);
        }

        if (keep.Count == 0 || keep.Count == tris.Length)
        {
            Debug.LogWarning("[密码箱] 顶面三角面没有识别出来（删除前后都是 " + (tris.Length / 3) + " 个），箱子保持原样。", this);
            return;
        }

        var opened = Instantiate(mesh);
        opened.name = mesh.name + "_开口";
        opened.SetTriangles(keep, 0);
        filter.sharedMesh = opened;

        // 箱子是单面网格，去掉顶面后内壁的背面会被剔除；这里开双面，箱子里才有一圈内壁。
        var renderer = filter.GetComponent<MeshRenderer>();
        if (renderer && renderer.sharedMaterial)
        {
            var material = renderer.material;
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);
        }

        Debug.Log("[密码箱] 已删除顶面：三角面 " + (tris.Length / 3) + " → " + (keep.Count / 3), this);
    }

    /// <summary>旧版本会在箱子上挂一块“密码箱盖子”，这里把它关掉。</summary>
    void HideLid()
    {
        if (lidHidden) return;
        lidHidden = true;

        foreach (var child in GetComponentsInChildren<Transform>(true))
            if (child != transform && child.name == "密码箱盖子")
                child.gameObject.SetActive(false);
    }

    static void SetColor(Material material, Color color)
    {
        if (!material) return;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        material.color = color;
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

    /// <summary>把场景里原本放在箱子里的碎片（Plane002）挂上拾取组件和碰撞体。</summary>
    void SetupFragment()
    {
        var envelope = fragment.GetComponent<EnvelopeFragment>();
        if (!envelope) envelope = fragment.AddComponent<EnvelopeFragment>();
        envelope.fragmentName = fragmentName;
        envelope.text = fragmentText;

        var pickup = fragment.GetComponent<PickupItem>();
        if (!pickup) pickup = fragment.AddComponent<PickupItem>();
        pickup.displayName = fragmentDisplayName;
        pickup.storyId = "letter_fragment_3";
        pickup.itemType = "信封碎片";
        pickup.description = fragmentText;

        // 纸片是单面片，翻个面就看不见了；开双面，并把纸自己的贴图当成自发光贴图。
        // 光是用一个均匀的 _EmissionColor 提亮，箱子里很暗，纸会变成一块没有图案的灰白方块，
        // 看不出是信封碎片；用贴图做自发光，暗处也能看清信封的纹理。
        var paper = fragment.GetComponentInChildren<Renderer>();
        if (paper)
        {
            var material = paper.material;
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);

            Texture albedo = null;
            if (material.HasProperty("_BaseMap")) albedo = material.GetTexture("_BaseMap");
            if (!albedo && material.HasProperty("_MainTex")) albedo = material.GetTexture("_MainTex");

            material.EnableKeyword("_EMISSION");
            if (albedo && material.HasProperty("_EmissionMap"))
            {
                material.SetTexture("_EmissionMap", albedo);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                // 0.2 左右刚好：暗房间里纸自己亮一点，纸上的字还留得住；调高会把整张纸烧成纯白。
                material.SetColor("_EmissionColor", new Color(.20f, .19f, .175f, 1f));
            }
            else
            {
                // 没有贴图可借：退回一点点均匀自发光，至少不会全黑。
                material.SetColor("_EmissionColor", new Color(.16f, .15f, .13f, 1f));
            }
        }

        if (!fragment.GetComponent<Collider>())
        {
            var scale = fragment.transform.lossyScale;
            var bounds = paper ? paper.bounds : new Bounds(fragment.transform.position, Vector3.one * .1f);
            var box = fragment.AddComponent<BoxCollider>();
            // 纸片又小又薄，拾取盒放大到 0.18m 见方，玩家瞄准箱口里的纸面就能按到。
            box.center = fragment.transform.InverseTransformPoint(bounds.center);
            box.size = new Vector3(
                Mathf.Max(.18f, bounds.size.x) / Mathf.Max(.0001f, Mathf.Abs(scale.x)),
                Mathf.Max(.12f, bounds.size.y) / Mathf.Max(.0001f, Mathf.Abs(scale.y)),
                Mathf.Max(.18f, bounds.size.z) / Mathf.Max(.0001f, Mathf.Abs(scale.z)));
        }

        // 箱子塞在矮柜格里，箱口大半藏在隔板的阴影里；纸片只靠自身一点自发光
        // 还是容易被当成一块白纸。和碎片①②一致：套一圈黄色描边，
        // 但不再额外打灯 —— 这么小的空间里点光只会把箱内壁烧成一片白。
        var outline = fragment.GetComponent<ProximityOutline>();
        if (!outline) outline = fragment.AddComponent<ProximityOutline>();
        // 用 Apply 而不是改字段：描边材质在 OnEnable 里就按默认青色建好了。
        outline.Apply(new Color(1f, .80f, .24f, .85f), .016f, 6f);
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
