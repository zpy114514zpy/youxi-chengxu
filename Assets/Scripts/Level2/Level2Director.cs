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

    public Level2Ui Ui { get; private set; }
    public Level2PhotoRepair Photo { get; private set; }
    public Level2Toys Toys { get; private set; }
    public Level2Mirror Mirror { get; private set; }
    public Transform Player { get; private set; }

    // 出生点：二楼二号房间正中偏南，面朝全家福那面墙。
    static readonly Vector3 Spawn = new Vector3(-19.7f, 3.95f, -103.6f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (SceneManager.GetActiveScene().name != "Level 2") return;
        if (FindObjectOfType<Level2Director>()) return;
        new GameObject("第二关总控").AddComponent<Level2Director>();
    }

    void Awake()
    {
        Instance = this;
        Ui = gameObject.AddComponent<Level2Ui>();
        HideHelperMeshes();
        SpawnPlayer();
        ApplyMood();

        Photo = gameObject.AddComponent<Level2PhotoRepair>(); Photo.Setup();
        Toys = gameObject.AddComponent<Level2Toys>(); Toys.Setup();
        Mirror = gameObject.AddComponent<Level2Mirror>(); Mirror.Setup();

        fogTimer = .4f;
        Invoke(nameof(Intro), .35f);
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

        Vector3 at = FindFreeSpot(go.transform);
        Vector3 look = new Vector3(-16.40f, at.y, -105.92f) - at;
        float yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
        var rot = Quaternion.Euler(0f, yaw, 0f);

        var body = go.GetComponent<Rigidbody>();
        if (body) { body.position = at; body.rotation = rot; body.velocity = Vector3.zero; }
        go.transform.SetPositionAndRotation(at, rot);
        Physics.SyncTransforms();

        Player = go.transform;
        Debug.Log($"[第二关] 玩家就位 {L2.Fmt(at)}，朝向 {yaw:F0}°");
        EnsureCamera();
    }

    /// <summary>出生点要留出一人高的空位，不然玩家一进去就被家具顶住。</summary>
    Vector3 FindFreeSpot(Transform ignore)
    {
        var offsets = new[]
        {
            Vector3.zero,
            new Vector3(0f, 0f, 1.2f), new Vector3(0f, 0f, -1.2f),
            new Vector3(1.2f, 0f, 0f), new Vector3(-1.2f, 0f, 0f),
            new Vector3(1.6f, 0f, 1.6f), new Vector3(-1.6f, 0f, -1.6f),
        };
        foreach (var o in offsets)
        {
            Vector3 p = Spawn + o;
            if (!Physics.Raycast(p + Vector3.up * 2.2f, Vector3.down, out var ground, 4f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
            Vector3 feet = ground.point;
            Vector3 bottom = feet + Vector3.up * .25f;
            Vector3 top = feet + Vector3.up * 1.55f;
            bool blocked = false;
            foreach (var c in Physics.OverlapCapsule(bottom, top, .26f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (c.isTrigger) continue;
                if (ignore && c.transform.IsChildOf(ignore)) continue;   // 玩家自己不算障碍
                blocked = true; break;
            }
            if (!blocked) return new Vector3(p.x, feet.y + (Spawn.y - 3.2f), p.z);
        }
        return Spawn;
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
        Ui.Say("这里是？", "这里是？我的房间？");
        Ui.Say("谜题一", "请完成谜题一：修补全家福。\n\n散落在地上的相片残片，一共三张。");
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
