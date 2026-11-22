using UnityEngine;

/// <summary>
/// 书桌前那把椅子：按 E 坐下 / 起身，按住 Q + WASD 还能推着走（PushProp）。
///
/// 坐下的实现方式：把角色钉在椅面上（复用 PlayerController 的“平台锁定”，关重力、每帧固定位置），
/// 再把第一人称视角压低一截，镜头就停在坐姿高度，正好对着桌面 —— 这也是目前没有坐姿动画时
/// 最省事、最不容易出 bug 的做法。
/// 坐下期间椅子自己的碰撞体会暂时关掉：角色是动态刚体，钉在椅面里会被物理系统顶出去。
/// </summary>
[DisallowMultipleComponent]
public sealed class ChairSeat : MonoBehaviour
{
    [Tooltip("离椅子多近能坐下（米）。")]
    public float reach = 1.7f;
    [Tooltip("坐下之后眼睛离地面多高（米）。站立时是 0.9，坐下压低到 0.72 左右最像坐在桌前。")]
    [Range(.4f, .9f)] public float seatedEyeHeight = .72f;
    [Tooltip("椅面大约离地多高（米）。射线找不到座面时用它兜底。")]
    public float seatHeight = .40f;

    PlayerController player;
    bool seated;
    Collider[] colliders;
    Vector3 seatPoint;

    static ChairSeat current;
    /// <summary>玩家当前坐着的那把椅子（没坐就是 null）。坐下时椅子的碰撞体是关着的，
    /// 准星扫不到它，所以要靠这个引用让“E 起身”一直可用。</summary>
    public static ChairSeat Current => current;

    public bool Seated => seated;

    public string Prompt
    {
        get
        {
            if (seated) return "E 起身";
            if (Near()) return "E 坐下";
            return GetComponent<PushProp>() ? "按住 Q + WASD 推动" : "";
        }
    }

    void Awake()
    {
        // 只记“当前是开着的”那些：装配脚本可能已经把凹的 MeshCollider 关掉了，
        // 起身时不该把它重新打开。
        var all = GetComponentsInChildren<Collider>(true);
        var live = new System.Collections.Generic.List<Collider>();
        foreach (var collider in all)
            if (collider && collider.enabled) live.Add(collider);
        colliders = live.ToArray();
    }

    public void Interact()
    {
        if (seated) { StandUp(); return; }
        if (!Resolve() || !Near()) return;
        Sit();
    }

    void Update()
    {
        // 玩家按方向键 / 被别人挪走：自动结束坐姿，别把角色永远钉在这。
        if (!seated) return;
        if (!Resolve() || !player.OnPlatform) { seated = false; current = null; RestoreColliders(); return; }
        player.KeepOnPlatform(new Vector3(seatPoint.x, seatPoint.y + .01f, seatPoint.z));
    }

    void Sit()
    {
        // 坐下时椅子自己的碰撞体要关掉（角色是动态刚体，钉在椅面里会被顶出去），
        // 所以座面高度必须先算好存下来 —— 关掉碰撞以后射线就量不到椅面了。
        seatPoint = SeatPoint();
        if (colliders != null)
            for (int i = 0; i < colliders.Length; i++)
                if (colliders[i]) colliders[i].enabled = false;

        // 角色是“站在椅面上”再压低视角的，所以下压量要按椅面高度倒推：
        // 站着时眼睛在 椅面+0.9，坐下要落到 地面+seatedEyeHeight。
        float floorY = BoundsOf(transform).min.y;
        float drop = Mathf.Clamp((seatPoint.y - floorY) + player.standingEyeHeight - seatedEyeHeight, 0f, .8f);

        player.SitOn(new Vector3(seatPoint.x, seatPoint.y + .01f, seatPoint.z), drop);
        seated = true;
        current = this;
        Debug.Log("[椅子] 坐上椅子：椅面 " + seatPoint.ToString("F2") + "，视角下压 " + drop.ToString("F2"), this);
    }

    void StandUp()
    {
        seated = false;
        if (current == this) current = null;
        RestoreColliders();
        if (player) player.StandUp();
    }

    void RestoreColliders()
    {
        if (colliders == null) return;
        for (int i = 0; i < colliders.Length; i++)
            if (colliders[i]) colliders[i].enabled = true;
    }

    void OnDisable() { RestoreColliders(); seated = false; if (current == this) current = null; }

    bool Resolve()
    {
        if (!player) player = FindObjectOfType<PlayerController>();
        return player != null;
    }

    bool Near()
    {
        if (!Resolve()) return false;
        Vector3 a = player.transform.position, b = transform.position;
        a.y = b.y = 0f;
        return Vector3.Distance(a, b) <= Mathf.Max(1.2f, reach);
    }

    /// <summary>
    /// 椅面中心：从正上方往下打几条射线，取最低的那个水平面当座面。
    /// 椅背、扶手都比座面高，只打一条很容易把“坐的位置”算到扶手上。
    /// </summary>
    Vector3 SeatPoint()
    {
        Bounds bounds = BoundsOf(transform);
        float fallback = bounds.min.y + Mathf.Min(seatHeight, bounds.size.y * .75f);
        Vector3 result = new Vector3(bounds.center.x, fallback, bounds.center.z);

        float step = Mathf.Max(.05f, Mathf.Min(bounds.extents.x, bounds.extents.z) * .45f);
        // 装配时补了一个外包 BoxCollider（给推动用的扫掠），它不是椅面：
        // 椅子本身有 MeshCollider 的话，量座面时跳过所有盒子。
        bool hasMesh = GetComponentInChildren<MeshCollider>(true) != null;

        float best = float.MaxValue;
        for (int i = 0; i < 5; i++)
        {
            float dx = i == 1 ? step : i == 2 ? -step : 0f;
            float dz = i == 3 ? step : i == 4 ? -step : 0f;
            Vector3 from = new Vector3(bounds.center.x + dx, bounds.max.y + .35f, bounds.center.z + dz);
            foreach (var hit in Physics.RaycastAll(from, Vector3.down, bounds.size.y + .8f,
                         Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (!IsMine(hit.collider)) continue;
                if (hasMesh && hit.collider is BoxCollider) continue;
                // 每列只取“自己的第一个面”；跨列再取最低的那个，椅背/扶手就会被椅面盖过去。
                if (hit.point.y < best)
                {
                    best = hit.point.y;
                    result = new Vector3(from.x, hit.point.y, from.z);
                }
                break;
            }
        }

        // 量出来的“椅面”明显比常规椅面高，说明打到的是椅背：退回按常见椅面高度算。
        if (best != float.MaxValue && best - bounds.min.y > seatHeight + .12f) result.y = fallback;
        return result;
    }

    bool IsMine(Collider other)
    {
        if (!other) return false;
        var t = other.transform;
        return t == transform || t.IsChildOf(transform);
    }

    static Bounds BoundsOf(Transform target)
    {
        var renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            var collider = target.GetComponent<Collider>();
            return collider ? collider.bounds : new Bounds(target.position, Vector3.one * .4f);
        }
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            if (renderers[i]) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }
}
