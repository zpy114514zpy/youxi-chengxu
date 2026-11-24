using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 第二关总控：只在 Level 2 生效。
///   · 生成玩家（复用第一关的角色预制体）与主相机
///   · 把整栋房子调成“冷灰 + 昏黄、灯很弱、偶尔闪”的旧屋氛围
///   · 依次装配三个谜题：修补全家福 / 归位残破玩具 / 镜前问答
/// 所有内容都在运行时构建，Level 1 的场景文件一个字都不改。
/// </summary>
public sealed class Level2Director : MonoBehaviour
{
    public static Level2Director Instance { get; private set; }

    /// <summary>读档时由 SaveSystem 在切场景前塞进来，Awake 之后按它恢复进度、不再播开场白。</summary>
    public static SaveData PendingRestore;

    public Level2Ui Ui { get; private set; }
    public Level2PhotoRepair Photo { get; private set; }
    public Level2Toys Toys { get; private set; }
    public Level2Mirror Mirror { get; private set; }
    public Level2Doors Doors { get; private set; }
    public Transform Player { get; private set; }

    // 出生点：二楼二号房里，沙发旁边那块空地（原来正落在沙发坐垫上）。
    static readonly Vector3 Spawn = new Vector3(-19.7f, 3.95f, -103.6f);
    const float FloorY = 3.20f;          // 二楼地板高度，站人的基准面

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        // 这个特性只在“游戏启动时”跑一次；读档会重新加载 Level 2，
        // 所以还要挂上 sceneLoaded，保证每次进第二关都有人把总控建起来。
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "Level 2") return;
        if (FindObjectOfType<Level2Director>()) return;
        new GameObject("第二关总控").AddComponent<Level2Director>();
    }

    void Awake()
    {
        Instance = this;
        Ui = gameObject.AddComponent<Level2Ui>();
        HideHelperMeshes();
        HideStrayBoard();
        SpawnPlayer();
        ApplyMood();

        Photo = gameObject.AddComponent<Level2PhotoRepair>(); Photo.Setup();
        Doors = gameObject.AddComponent<Level2Doors>(); Doors.Setup();
        Toys = gameObject.AddComponent<Level2Toys>(); Toys.Setup();
        Mirror = gameObject.AddComponent<Level2Mirror>(); Mirror.Setup();

        Invoke(nameof(AuditFloorClutter), 1.6f);

        fogTimer = .4f;
    }

    void Start()
    {
        if (PendingRestore != null)
        {
            var data = PendingRestore;
            PendingRestore = null;
            ApplySave(data);
        }
        else Invoke(nameof(Intro), .35f);
    }

    // ---------------- 存档 ----------------

    public void FillSave(SaveData d)
    {
        if (Photo) d.photoPlaced = Photo.PlacedCount;
        if (Toys) d.toys = Toys.CaptureState();
        if (Mirror) d.mirrorStage = Mirror.Stage;
        if (Doors) d.doors = Doors.CaptureState();
    }

    public void ApplySave(SaveData d)
    {
        if (Doors) Doors.RestoreState(d.doors);
        if (Photo) Photo.RestoreProgress(d.photoPlaced);
        if (Toys) Toys.RestoreState(d.toys);
        if (Mirror) Mirror.RestoreStage(d.mirrorStage);
        // 相框已经补完 = 谜题二必然已经开放。存档里万一漏了这一个位（比如补完的同一帧退出），
        // 这里兜一次，避免玩家读档回来发现洗手台和摆放点全都没反应。
        if (Photo && Photo.Repaired && Toys && !Toys.Unlocked) Toys.Unlock();
        Debug.Log($"[第二关] 读档完成：相片 {d.photoPlaced} 块 / 镜子 {d.mirrorStage} / 门 {d.doors}");
    }

    // ---------------- 玩家 ----------------

    void SpawnPlayer()
    {
        // 角色实例已经摆在 Level 2 场景里（从第一关复制的同一套建模和动画），
        // 这里只负责把它挪到二号房间的空位上、并对准全家福那面墙。
        var go = GameObject.Find("Player");
        if (!go)
        {
            Debug.LogError("[第二关] 场景里没有 Player，无法控制角色。");
            return;
        }

        Vector3 feet = FindFloorSpot(go);
        Vector3 look = new Vector3(-16.40f, 1.2f, -105.92f) - new Vector3(feet.x, 1.2f, feet.z);
        float yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
        var rot = Quaternion.Euler(0f, yaw, 0f);

        // 先按“脚踩在地板上”摆一次，再量出模型真正的脚底，把高度补正回来。
        go.transform.SetPositionAndRotation(new Vector3(feet.x, FloorY, feet.z), rot);
        Physics.SyncTransforms();
        float meshFeet = MeshFeetY(go);
        AlignCapsuleToFeet(go);                       // 胶囊底对齐模型脚底，否则物理会把脚顶进地板
        float rootY = FloorY + (FloorY - meshFeet);
        var at = new Vector3(feet.x, rootY, feet.z);

        var body = go.GetComponent<Rigidbody>();
        if (body)
        {
            body.position = at;
            body.rotation = rot;
            body.velocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        go.transform.SetPositionAndRotation(at, rot);
        Physics.SyncTransforms();

        Player = go.transform;
        Debug.Log($"[第二关] 玩家就位 {L2.Fmt(at)}，朝向 {yaw:F0}°，模型脚底 {meshFeet:F3}（地板 {FloorY:F2}）");
        EnsureCamera();
    }

    /// <summary>模型真正踩地的那一层：蒙皮网格的最低点（有阴影片的话一起算，差不了多少）。</summary>
    static float MeshFeetY(GameObject go)
    {
        float min = float.MaxValue;
        foreach (var sm in go.GetComponentsInChildren<SkinnedMeshRenderer>())
            if (sm.enabled) min = Mathf.Min(min, sm.bounds.min.y);
        if (min < float.MaxValue) return min;

        foreach (var r in go.GetComponentsInChildren<Renderer>())
            if (r.enabled) min = Mathf.Min(min, r.bounds.min.y);
        return min < float.MaxValue ? min : 0f;
    }

    /// <summary>
    /// 这套人物模型的脚底比胶囊底低 14cm，物理把胶囊放在地上时，脚就陷进地板里了。
    /// 把胶囊往下挪同样的距离，让“胶囊底 = 脚底”，站稳之后脚正好贴在地板上。
    /// </summary>
    static void AlignCapsuleToFeet(GameObject go)
    {
        var cap = go.GetComponentInChildren<CapsuleCollider>();
        if (!cap) return;

        float feet = MeshFeetY(go);
        float gap = cap.bounds.min.y - feet;          // 胶囊底比脚底高多少
        if (Mathf.Abs(gap) < .02f) return;

        float sy = Mathf.Max(.0001f, Mathf.Abs(cap.transform.lossyScale.y));
        cap.center -= new Vector3(0f, gap / sy, 0f);
        Physics.SyncTransforms();
        Debug.Log($"[第二关] 胶囊底对齐脚底，下移 {gap:F3}m");
    }

    /// <summary>
    /// 在沙发旁找一块真正空着的地板：脚下要有二楼地板，且一人高的胶囊不能被家具顶住。
    /// 优先往玩家右手边（-Z）挪，那块是房间中央的空地。
    /// </summary>
    Vector3 FindFloorSpot(GameObject player)
    {
        var offsets = new[]
        {
            new Vector3(0f, 0f, -1.5f), new Vector3(.5f, 0f, -1.5f), new Vector3(-.5f, 0f, -1.5f),
            new Vector3(1.2f, 0f, -.8f), new Vector3(1.2f, 0f, .6f),
            new Vector3(-1.2f, 0f, -.8f), new Vector3(0f, 0f, 1.5f),
            new Vector3(0f, 0f, 2.2f), new Vector3(-1.8f, 0f, -1.8f), new Vector3(1.8f, 0f, -1.8f),
        };

        foreach (var o in offsets)
        {
            var p = new Vector3(Spawn.x + o.x, FloorY, Spawn.z + o.z);

            // 脚下必须是这块地板，不能是沙发、地毯边或楼下的什么东西
            if (!Physics.Raycast(p + Vector3.up * .45f, Vector3.down, out var ground, .8f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
            if (Mathf.Abs(ground.point.y - FloorY) > .06f) continue;

            // 一人高的位置不能被家具占着（沙发、扶手椅、书桌都算）
            bool blocked = false;
            Vector3 low = p + Vector3.up * .18f, high = p + Vector3.up * 1.55f;
            foreach (var c in Physics.OverlapCapsule(low, high, .27f,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (c.isTrigger || !c.enabled) continue;
                if (player && c.transform.IsChildOf(player.transform)) continue;
                if (c.bounds.max.y <= FloorY + .06f) continue;      // 地毯这种贴地薄片不算
                blocked = true;
                break;
            }
            if (blocked) continue;

            Debug.Log($"[第二关] 出生点 {L2.Fmt(p)}（偏移 {L2.Fmt(o)}）");
            return p;
        }

        Debug.LogWarning("[第二关] 没找到空地板，退回房间中央");
        return new Vector3(Spawn.x, FloorY, Spawn.z - 1.5f);
    }

    void EnsureCamera()
    {
        Camera cam = Camera.main;
        if (!cam)
        {
            var go = new GameObject("Main Camera");
            cam = go.AddComponent<Camera>();
            go.AddComponent<AudioListener>();
        }
        cam.gameObject.tag = "MainCamera";
        if (cam.transform.parent) cam.transform.SetParent(null, true);
        cam.nearClipPlane = .02f;
        cam.farClipPlane = 1000f;
        cam.fieldOfView = 68f;
        cam.transform.SetPositionAndRotation(Player ? Player.position + Vector3.up * .17f : Spawn,
            Player ? Player.rotation : Quaternion.identity);
    }

    /// <summary>
    /// Unreal 导进来的场景里，ReflectionCapture / PointLight / SpotLight 这些组件自带一块
    /// “图标模型”（灯泡、灰圆球那种），在编辑器里当辅助标记用，进游戏就变成满屋子飘的假灯。
    /// 这里只关渲染器，光源本身照常工作。
    /// </summary>
    void HideHelperMeshes()
    {
        string[] keys = { "ReflectionCapture", "ReflectionCaptureProbe", "PointLight", "SpotLight",
                          "RectLight", "LightComponent", "SphereReflection", "BoxReflection" };
        int hidden = 0;
        foreach (var t in FindObjectsOfType<Transform>(true))
        {
            bool match = false;
            foreach (var k in keys)
                if (t.name.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0) { match = true; break; }
            if (!match) continue;

            foreach (var r in t.GetComponentsInChildren<Renderer>(true))
            {
                if (!r || !r.enabled) continue;
                r.enabled = false;
                hidden++;
            }
        }
        Debug.Log($"[第二关] 隐藏编辑器辅助图标渲染器 {hidden} 个");
    }

    /// <summary>
    /// 出生房间（二楼二号房）里斜靠着一块软木公告板 SM_Board_A1（场景坐标约 -20.86, 3.61, -105.06）。
    /// 它和三个谜题都没关系，就是美术摆的一件装饰，可它正好杵在出生点脚边、贴在眼前的视野下方，
    /// 玩家一低头就看见一大块板子。这里整个关掉（只关当前这一局，场景文件不动，编辑器里还在）。
    /// </summary>
    void HideStrayBoard()
    {
        int n = 0;
        foreach (var t in FindObjectsOfType<Transform>(true))
        {
            if (!t.name.StartsWith("SM_Board_A1", System.StringComparison.OrdinalIgnoreCase)) continue;
            if (!t.gameObject.scene.IsValid()) continue;
            if (!t.gameObject.activeInHierarchy) continue;   // 父节点已经关掉，子节点跟着算，不重复计数
            t.gameObject.SetActive(false);
            n++;
        }
        Debug.Log(n > 0
            ? $"[第二关] 已隐藏出生房间的软木公告板 SM_Board_A1 ×{n}"
            : "[第二关] 没找到 SM_Board_A1（可能已经被藏起来了）");
    }

    /// <summary>
    /// 临时排查（定位用，不动任何物件）：把出生点 10 米内“又薄又平、贴在地面这一层”的渲染器
    /// 全部打进日志。地上那块多余的深色片看截图认不出是哪个物件，靠这份清单点名。
    /// </summary>
    void AuditFloorClutter()
    {
        if (!Player) return;
        Vector3 origin = Player.position;
        int n = 0;
        foreach (var r in FindObjectsOfType<Renderer>(true))
        {
            if (!r || !r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (r is ParticleSystemRenderer || r is LineRenderer || r is TrailRenderer) continue;
            var b = r.bounds;
            if ((b.center - origin).sqrMagnitude > 100f) continue;           // 只看 10 米内
            Vector3 s = b.size;
            float thin = Mathf.Min(s.x, Mathf.Min(s.y, s.z));
            if (thin > .35f) continue;                                        // 只关心薄片
            if (b.min.y > FloorY + .8f || b.max.y < FloorY - .25f) continue;   // 只关心地面这一层
            float area = Mathf.Max(s.x * s.y, Mathf.Max(s.y * s.z, s.z * s.x));
            if (area < .35f) continue;                                        // 小摆件忽略
            string mat = r.sharedMaterial ? r.sharedMaterial.name : "(无材质)";
            bool col = r.GetComponent<Collider>() || r.GetComponentInParent<Collider>();
            Debug.Log($"[地面排查] {PathOf(r.transform)} 中心 {L2.Fmt(b.center)} 尺寸 {s.x:F2}×{s.y:F2}×{s.z:F2} 面积 {area:F2}m² 材质 {mat} 碰撞体 {(col ? "有" : "无")}");
            n++;
        }
        Debug.Log($"[地面排查] 出生点 {L2.Fmt(origin)} 附近的薄片候选 {n} 个");

        // 再撒一遍向下的射线，看脚下这块地板是不是整片都在、高度是不是都在 3.20 上下 ——
        // 万一那块深色是“地板缺了一块、直接看到楼下”，薄片清单里就不会有任何东西，靠这一步兜住。
        for (int i = -2; i <= 2; i++)
        {
            for (int j = -2; j <= 2; j++)
            {
                Vector3 from = origin + new Vector3(i * 1.6f, 1.4f, j * 1.6f);
                if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 6f, ~0, QueryTriggerInteraction.Ignore))
                    Debug.Log($"[地面排查] 射线 ({i},{j}) 命中 {hit.collider.name} 高度 {hit.point.y:F3} 距离 {hit.distance:F2}");
                else
                    Debug.LogWarning($"[地面排查] 射线 ({i},{j}) 落空 —— 这里没有地板");
            }
        }
    }

    static string PathOf(Transform t)
    {
        var sb = new System.Text.StringBuilder(t.name);
        for (var p = t.parent; p; p = p.parent) sb.Insert(0, p.name + "/");
        return sb.ToString();
    }

    // ---------------- 氛围 ----------------

    void ApplyMood()
    {
        // 环境光压暗、往冷灰偏：这一关不再有第一关那种米黄柔光。
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.19f, .21f, .25f);
        RenderSettings.ambientEquatorColor = new Color(.13f, .14f, .17f);
        RenderSettings.ambientGroundColor = new Color(.07f, .07f, .09f);

        var lights = new List<Light>();
        foreach (var l in FindObjectsOfType<Light>(true))
            if (l && l.enabled && l.type != LightType.Rectangle) lights.Add(l);
        lights.Sort((a, b) => b.intensity.CompareTo(a.intensity));

        int index = 0;
        foreach (var l in lights)
        {
            float before = l.intensity;
            switch (l.type)
            {
                case LightType.Directional:
                    // 窗外不再有明亮阳光，只剩一层灰白天光。
                    l.color = new Color(.72f, .78f, .86f);
                    l.intensity = Mathf.Min(before * .16f, .22f);
                    l.shadows = LightShadows.Soft;
                    break;

                case LightType.Spot:
                    // 吸顶射灯：降功率、偏冷。阴影留着，旧屋的味道靠阴影撑。
                    l.color = Color.Lerp(l.color, new Color(.82f, .87f, 1f), .55f);
                    l.intensity = before * .34f;
                    l.shadows = LightShadows.Soft;
                    break;

                default:
                    // 台灯/壁灯：留一点昏黄，作为整间冷灰里唯一的暖。
                    l.color = Color.Lerp(l.color, new Color(1f, .84f, .58f), .45f);
                    l.intensity = before * .55f;
                    break;
            }
            l.range *= 1.05f;

            // 每隔 4 盏挑一盏做接触不良的闪烁。
            if (before > .3f && index % 4 == 1)
                l.gameObject.AddComponent<L2Flicker>().baseIntensity = l.intensity;
            index++;
        }
        Debug.Log($"[第二关] 氛围已调暗：共处理 {lights.Count} 盏灯");

        // 主相机背景压成冷灰，不至于一开门看到白得刺眼的天空。
        var cam = Camera.main;
        if (cam)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(.80f, .83f, .86f);
        }
    }

    float fogTimer;

    void Update()
    {
        // FutureFog 在 PlayerController.Start 里才设雾，这里每隔几秒把它盖回“屋里清楚、屋外白雾”。
        fogTimer -= Time.deltaTime;
        if (fogTimer <= 0f) { fogTimer = 2.5f; ApplyFog(); }

        CheckDownstairs();
    }

    // ---------------- 下楼 ----------------

    bool wasUpstairs, downstairsNoticed;

    /// <summary>
    /// 玩家第一次从二楼走楼梯下到客厅时给一句提示。
    /// 高度判定就够了：这栋房子只有那道楼梯能下楼，二楼地板 3.20、一楼地面 0。
    /// 只提示一次；读档直接落在一楼时 wasUpstairs 还是 false，不会误触发。
    /// </summary>
    void CheckDownstairs()
    {
        if (downstairsNoticed || !Player) return;

        float y = Player.position.y;
        if (y > 3.0f) { wasUpstairs = true; return; }
        if (!wasUpstairs || y > 1.45f) return;

        downstairsNoticed = true;
        Ui?.Say("", "客厅里，好像多了一个东西。");
        Debug.Log($"[第二关] 下到一楼 {L2.Fmt(Player.position)}，弹出提示");
    }

    void ApplyFog()
    {
        // FutureFog 会把它自己的雾参数写进来，这里往远处推一点：
        // 屋内要看得清，只有窗外和楼下大门外才该是一片朦胧白雾。
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(.88f, .90f, .93f);
        RenderSettings.fogStartDistance = 13f;
        RenderSettings.fogEndDistance = 36f;

        var cam = Camera.main;
        if (cam)
        {
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = RenderSettings.fogColor;
        }
    }

    // ---------------- 开场 ----------------

    void Intro()
    {
        // 开场四页：先是一头雾水，再让玩家去看看，最后把“改造这个家”交给他，末尾接谜题一的目标。
        Ui.Say("疑惑", "我这是又来到了哪儿？");
        Ui.Say("四处看看", "你可以四处看看周围的环境怎么样。");
        Ui.Say("动手吧", "是不是很糟糕了，跟你之前的家相比差距很大，那么现在由你来改造他们吧！");
        Ui.Say("谜题一", "请完成谜题一：修补全家福。\n\n散落在地上的相片残片，一共三张。\n\n其余的事，等我先把这张照片补好再说。");
    }
}

/// <summary>接触不良的灯泡：亮度在基线附近轻微抖动，偶尔狠狠暗一下。</summary>
public sealed class L2Flicker : MonoBehaviour
{
    public float baseIntensity = 1f;
    Light lamp;
    float seed, dip;

    void Awake()
    {
        lamp = GetComponent<Light>();
        seed = Random.Range(0f, 100f);
    }

    void Update()
    {
        if (!lamp) return;
        float t = Time.time * 1.4f + seed;
        float noise = Mathf.PerlinNoise(t, seed * .37f);
        if (dip <= 0f && noise < .28f) dip = Random.Range(.07f, .20f);
        if (dip > 0f) dip -= Time.deltaTime;

        float k = dip > 0f ? .12f : Mathf.Lerp(.82f, 1f, noise);
        lamp.intensity = baseIntensity * k;
    }
}
