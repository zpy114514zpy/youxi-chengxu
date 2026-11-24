using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 第一关结尾的传送门：碎片放好之后，玩家**身后的墙上**亮起一道能包住人的门框。
///
/// 为什么是一道“画在墙上的门框”而不是一个悬空光环：
/// 这一关的叙事是“旧信撬开一道时光夹缝”，缝应该开在房子本来的墙上，
/// 而不是凭空立一个发光圆环（那属于另一套美术语言）。
///
/// 做法：从玩家胸口朝正后方扇形打射线，找最近的一面墙；
/// 在墙面贴一张门形框线的面片（House/PortalFrame，中心全透明、只有一圈暖黄框线），
/// 不改场景网格，也不动任何原有物件。走近按 E 依然直接切第二关。
/// </summary>
[DisallowMultipleComponent]
public sealed class BlackHolePortal : MonoBehaviour
{
    /// <summary>门框能包住人的尺寸：宽 1.05m、高 2.10m（角色高约 1.8m）。</summary>
    const float HoleWidth = 1.05f;
    const float HoleHeight = 2.10f;

    static readonly Color FadeColor = new Color(.008f, .006f, .005f, 1f);

    public string nextScene = "Level 2";

    Transform hole;
    Material holeMat;
    Light[] glows;
    float[] glowBase;
    float bornAt;
    bool leaving;
    bool near;
    PlayerController player;
    Font font;

    PlayerController Player
    {
        get
        {
            if (!player) player = FindObjectOfType<PlayerController>();
            return player;
        }
    }

    // ---------------------------------------------------------------- 生成

    /// <summary>
    /// 在玩家身后开洞。player 为空时退回到 fallback 点旁边，保证流程不会断。
    /// </summary>
    public static BlackHolePortal OpenBehind(Transform player, Vector3 fallback, string scene)
    {
        Vector3 point, normal;
        float distance;
        bool hasWall = FindWallBehind(player, out point, out normal, out distance);

        Vector3 back = player ? -Vector3.ProjectOnPlane(player.forward, Vector3.up) : Vector3.back;
        if (back.sqrMagnitude < .001f) back = Vector3.back;
        back.Normalize();

        Vector3 center;
        Vector3 facing;
        if (hasWall)
        {
            float floorY = FloorY(player, point);
            center = new Vector3(point.x, floorY + HoleHeight * .5f - .02f, point.z) + normal * .020f;
            facing = normal;
        }
        else
        {
            // 身后没有可用的墙（站位太偏）：仍然给一个能用的洞，只是不贴墙。
            float floorY = FloorY(player, fallback);
            center = fallback + back * 1.9f;
            center.y = floorY + HoleHeight * .5f;
            facing = -back;
            Debug.LogWarning("[传送门] 玩家身后没找到墙，改用悬空门框：" + center.ToString("F2"));
        }

        var go = new GameObject("墙上的门（通往第二关）");
        go.transform.SetPositionAndRotation(center, Quaternion.LookRotation(facing, Vector3.up));

        var portal = go.AddComponent<BlackHolePortal>();
        portal.nextScene = scene;
        portal.Build();
        portal.bornAt = Time.time;

        if (hasWall)
            Debug.Log($"[传送门] 已在墙面上亮起门框：墙面点 {point:F2}，法线 {normal:F2}，距离玩家 {distance:F2}m");
        return portal;
    }

    /// <summary>
    /// 从玩家胸口朝正后方 ±50° 扫 11 条射线，挑最近的一面“够高够平”的墙。
    /// 先只找 3.2m 内的（贴着走廊的那面墙最好），找不到再把距离放到 8m。
    /// 矮家具、地板、天花板、门扇、玩家自己都不算。
    /// </summary>
    static bool FindWallBehind(Transform player, out Vector3 point, out Vector3 normal, out float distance)
    {
        if (FindWallPass(player, 3.2f, out point, out normal, out distance)) return true;
        return FindWallPass(player, 8.0f, out point, out normal, out distance);
    }

    static bool FindWallPass(Transform player, float maxDistance,
        out Vector3 point, out Vector3 normal, out float distance)
    {
        point = Vector3.zero;
        normal = Vector3.zero;
        distance = 0f;
        if (!player) return false;

        Vector3 back = -Vector3.ProjectOnPlane(player.forward, Vector3.up);
        if (back.sqrMagnitude < .001f) back = Vector3.back;
        back.Normalize();

        Vector3 origin = player.position + Vector3.up * 1.05f;
        float bestScore = float.MaxValue;

        for (int i = -5; i <= 5; i++)
        {
            float angle = i * 10f;
            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * back;

            float nearest = float.MaxValue;
            RaycastHit bestHit = default;
            bool found = false;

            // RaycastAll 不保证按距离排序，自己挑最近的。
            foreach (var hit in Physics.RaycastAll(origin, dir, maxDistance,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (!hit.collider || hit.collider.transform.IsChildOf(player)) continue;
                if (Mathf.Abs(hit.normal.y) > .45f) continue;          // 地板 / 天花板
                if (hit.distance < .55f) continue;                     // 贴脸了
                if (hit.collider.bounds.size.y < 1.5f) continue;        // 矮家具不是墙
                // 衣帽架、落地灯、窄柜子也够高，但不是墙：又窄又厚的一律不算。
                // 墙面的特征是“某一边很薄”（墙体、护墙板、贴条），所以薄片仍然放行。
                var b = hit.collider.bounds;
                if (Mathf.Max(b.size.x, b.size.z) < 1.2f && Mathf.Min(b.size.x, b.size.z) > .35f) continue;
                if (hit.collider.GetComponentInParent<InteractiveDoor>()) continue;  // 别把洞开在门上
                if (hit.distance < nearest) { nearest = hit.distance; bestHit = hit; found = true; }
            }

            if (!found) continue;

            // 越接近正后方越好，其次越近越好。
            float score = nearest + Mathf.Abs(angle) * .014f;
            if (score < bestScore)
            {
                bestScore = score;
                point = bestHit.point;
                normal = bestHit.normal;
                distance = nearest;
            }
        }

        return bestScore < float.MaxValue;
    }

    /// <summary>
    /// 洞口正下方的地板高度。
    ///
    /// 这里踩过一个很隐蔽的坑：原来的写法是从“墙面点上方 1.4m”往下打 6m。
    /// 可这栋房子层高只有两米八到三米，起点直接穿到了上一层楼板的上表面，
    /// 量回来的“地面”其实是楼上的地板 —— 门框就被抬到 7.27m，整个飘进天花板里，
    /// 玩家站在 3.1m 的走廊里当然什么都看不见（日志里 center.y=7.28 就是这么来的）。
    ///
    /// 现在以玩家胶囊底（就是脚底）为准，只在脚边做一次 0.9m 的短射线校正。
    /// </summary>
    static float FloorY(Transform player, Vector3 at)
    {
        if (player)
        {
            var cap = player.GetComponentInChildren<CapsuleCollider>();
            if (cap)
            {
                float feet = cap.bounds.min.y;
                if (Physics.Raycast(new Vector3(at.x, feet + .30f, at.z), Vector3.down, out var under, .90f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    return under.point.y;
                return feet;
            }
        }

        // 没有玩家（理论上不会走到这里）：只从墙面点往下找，绝不从墙面点“上方”起射线。
        if (Physics.Raycast(new Vector3(at.x, at.y - .05f, at.z), Vector3.down, out var hit, 4f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return hit.point.y;

        return at.y - 1.6f;
    }

    void Build()
    {
        var shader = Resources.Load<Shader>("Shaders/PortalFrame");
        if (!shader) shader = Shader.Find("House/PortalFrame");
        if (!shader)
        {
            Debug.LogError("[传送门] 找不到 House/PortalFrame 着色器，退化成一层半透明黄面。");
            holeMat = L2.Unlit(new Color(1f, .78f, .30f, .45f), transparent: true);
        }
        else
        {
            holeMat = new Material(shader) { hideFlags = HideFlags.DontSave };
            holeMat.SetColor("_FrameColor", new Color(1f, .78f, .30f, 1f));
            holeMat.SetFloat("_FrameStrength", 1.05f);
            holeMat.SetFloat("_BandCenter", .90f);
            holeMat.SetFloat("_BandWidth", .038f);
            holeMat.SetFloat("_Softness", .010f);
            holeMat.SetFloat("_InnerGlow", .22f);
            holeMat.SetFloat("_Corner", 5f);
        }

        // 面片比门洞略大一圈：框线落在 1.05×2.10 的洞口位置上，多出来的是渐隐余量。
        hole = L2.Quad("墙上的门", transform, transform.position,
            new Vector2(HoleWidth * 1.16f, HoleHeight * 1.10f), holeMat, transform.rotation);

        // 门框三盏暖光：只在墙面附近铺一层“这里被点亮了”的感觉，不照亮房间。
        glows = new Light[3];
        glowBase = new float[3];
        for (int i = 0; i < 3; i++)
        {
            var host = new GameObject("门框微光" + i);
            host.transform.SetParent(transform, false);
            host.transform.position = transform.position
                + transform.forward * .22f
                + Vector3.up * (i - 1) * (HoleHeight * .30f);

            var lamp = host.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = new Color(1f, .78f, .34f);
            lamp.range = 1.8f;
            lamp.shadows = LightShadows.None;
            glows[i] = lamp;
            glowBase[i] = .20f;
            lamp.intensity = glowBase[i];
        }

        font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 18);
    }

    // ---------------------------------------------------------------- 交互

    void Update()
    {
        float t = Time.time - bornAt;

        // 框线一明一暗地呼吸，像门后面有东西在等着。
        if (holeMat && holeMat.HasProperty("_FrameStrength"))
            holeMat.SetFloat("_FrameStrength", .95f + .20f * Mathf.Sin(t * 1.15f));
        if (glows != null)
            for (int i = 0; i < glows.Length; i++)
                if (glows[i]) glows[i].intensity = glowBase[i] * (.85f + .25f * Mathf.Sin(t * 1.15f + i * .9f));

        if (leaving) return;
        var who = Player;
        if (!who) return;

        near = Vector3.Distance(who.transform.position, transform.position) < 3.6f;

        if (!near) return;
        if (!Input.GetKeyDown(KeyCode.E)) return;
        if (UiInputBlocker.Active) return;
        if (StoryProgress.Instance && StoryProgress.Instance.IsReading) return;   // 先把提示页看完
        if (Cursor.lockState != CursorLockMode.Locked) return;

        Enter();
    }

    void Enter()
    {
        leaving = true;
        GameAudio.PlayPortal();
        Debug.Log("[传送门] 玩家穿过门框，前往 " + nextScene);
        ScreenFader.ToScene(nextScene, FadeColor, 1.5f, 1.6f);
        StartCoroutine(HideSelf());
    }

    IEnumerator HideSelf()
    {
        yield return new WaitForSecondsRealtime(.35f);
        if (hole) hole.gameObject.SetActive(false);
    }

    void OnGUI()
    {
        if (leaving) return;
        if (!near) return;
        if (StoryProgress.Instance && StoryProgress.Instance.IsShowingPage) return;

        if (!font) font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 18);
        var style = new GUIStyle(GUI.skin.box)
        {
            font = font,
            fontSize = 17,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
        };

        float width = Mathf.Min(520f, Screen.width * .8f);
        var rect = new Rect((Screen.width - width) * .5f, Screen.height * .80f, width, 50f);
        GUI.Box(rect, "E 走进那道门", style);
    }

    void OnDestroy()
    {
        if (holeMat) Destroy(holeMat);
        if (font) Destroy(font);
    }
}
