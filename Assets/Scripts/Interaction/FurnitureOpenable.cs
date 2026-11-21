using UnityEngine;

// Place on an individual cabinet leaf or drawer, not on the entire cabinet.
public sealed class FurnitureOpenable : MonoBehaviour
{
    public bool slide;
    public Vector3 slideDirection = Vector3.forward;
    public float travel = .28f;
    public float openAngle = 75f;
    public float speed = 2f;

    Vector3 closedPosition;
    Quaternion closedRotation;
    float amount;
    bool opened;
    Vector3 openingDirection;

    public string Prompt => slide ? "E 打开 / 关闭抽屉" : "E 打开 / 关闭柜门";

    void Awake()
    {
        closedPosition = transform.position;
        closedRotation = transform.rotation;
        if (slideDirection.sqrMagnitude < .001f) slideDirection = Vector3.forward;
        slideDirection.Normalize();
        openingDirection = slideDirection;
    }

    /// <summary>面板的视觉中心。抽屉模型的 pivot 常常全都压在家具原点上，不能用 transform.position。</summary>
    Vector3 Center()
    {
        var visual = GetComponent<Renderer>();
        if (visual) return visual.bounds.center;
        var child = GetComponentInChildren<Renderer>();
        return child ? child.bounds.center : transform.position;
    }

    static float Volume(Vector3 size) => Mathf.Max(.0001f, size.x * size.y * size.z);

    /// <summary>最薄的那根轴；最薄的是竖直轴时返回 zero（说明这是抽屉盒，不是薄面板）。</summary>
    static Vector3 ThinHorizontalAxis(Bounds b)
    {
        Vector3 s = b.size;
        if (s.y <= s.x && s.y <= s.z) return Vector3.zero;
        return s.x <= s.z ? Vector3.right : Vector3.forward;
    }

    /// <summary>柜面的水平法线（无正负）。</summary>
    Vector3 FurnitureAxis(Vector3 center)
    {
        // ① 薄面板（门板、抽屉面板、薄抽屉盒）自身的最薄水平轴就是柜面法线。
        var visual = GetComponent<Renderer>();
        if (visual)
        {
            Vector3 thin = ThinHorizontalAxis(visual.bounds);
            if (thin != Vector3.zero) return thin;
        }

        // ② 抽屉盒（最薄的是高度）：用整件家具的包围盒。柜子总是“深 &lt; 宽”，
        //    水平方向较短的那条轴就是柜面法线。这一条对两列抽屉的电视柜同样成立。
        if (TryBodyBounds(center, out var body))
            return body.size.x <= body.size.z ? Vector3.right : Vector3.forward;

        // ③ 再不行就看同一件家具的其他面板（附近 1.5m、同高度）合并出来的框。
        var merged = new Bounds(center, Vector3.one * .05f);
        int count = 0;
        foreach (var other in FindObjectsOfType<FurnitureOpenable>())
        {
            if (other == this || !other) continue;
            var r = other.GetComponent<Renderer>() ?? other.GetComponentInChildren<Renderer>();
            if (!r) continue;
            Vector3 c = r.bounds.center;
            if (Mathf.Abs(c.y - center.y) > .9f) continue;
            if (Vector2.Distance(new Vector2(c.x, c.z), new Vector2(center.x, center.z)) > 1.5f) continue;
            merged.Encapsulate(r.bounds);
            count++;
        }
        if (count > 0) return merged.size.x <= merged.size.z ? Vector3.right : Vector3.forward;

        return Vector3.zero;
    }

    /// <summary>
    /// 家具整体包围盒：顺着父链找第一个明显比自己大的渲染器。
    /// 抽屉的父节点常常只是空壳（没有渲染器），所以这里一路往上找，但拒绝“一整间房”那种超大框。
    /// </summary>
    bool TryBodyBounds(Vector3 center, out Bounds body)
    {
        body = new Bounds(center, Vector3.one);
        var visual = GetComponent<Renderer>();
        float selfVolume = visual ? Volume(visual.bounds.size) : .0005f;

        for (var t = transform.parent; t; t = t.parent)
        {
            var r = t.GetComponent<Renderer>();
            if (!r) continue;
            if (Volume(r.bounds.size) < selfVolume * 3f) continue;
            if (r.bounds.size.magnitude > 3.5f) break;     // 再往上就是一整间屋子了
            body = r.bounds;
            return true;
        }
        return false;
    }

    /// <summary>
    /// “朝外”方向 = 柜面法线 + 朝向玩家。
    /// 玩家必须站在柜子前面才能用 E 交互，所以“朝向玩家”就是“往外”。
    /// </summary>
    Vector3 Outward(Vector3 playerPosition)
    {
        Vector3 center = Center();
        Vector3 axis = FurnitureAxis(center);

        Vector3 toPlayer = Vector3.ProjectOnPlane(playerPosition - center, Vector3.up);

        if (axis == Vector3.zero)
        {
            // 拿不到柜体几何：退化成“朝玩家最接近的那条轴”。
            if (toPlayer.sqrMagnitude < .0004f)
                return slideDirection.sqrMagnitude > .001f ? slideDirection : Vector3.forward;
            return Mathf.Abs(toPlayer.x) >= Mathf.Abs(toPlayer.z)
                ? new Vector3(Mathf.Sign(toPlayer.x), 0f, 0f)
                : new Vector3(0f, 0f, Mathf.Sign(toPlayer.z));
        }

        if (toPlayer.sqrMagnitude < .0004f) return axis;

        // 玩家在侧面（夹角太大）时，用“柜体中心 → 面板中心”定符号，比朝玩家可靠。
        if (Mathf.Abs(Vector3.Dot(axis, toPlayer.normalized)) < .28f)
        {
            Vector3 fromBody = Vector3.ProjectOnPlane(center - BodyCenter(center), Vector3.up);
            if (fromBody.sqrMagnitude > .0004f && Vector3.Dot(axis, fromBody) != 0f)
                return axis * Mathf.Sign(Vector3.Dot(axis, fromBody));
        }

        return axis * (Vector3.Dot(axis, toPlayer) >= 0f ? 1f : -1f);
    }

    Vector3 BodyCenter(Vector3 fallback)
    {
        if (TryBodyBounds(fallback, out var body)) return body.center;
        return transform.parent ? transform.parent.position : fallback;
    }

    static Vector3 PlayerPosition
    {
        get
        {
            var player = FindObjectOfType<PlayerController>();
            if (player) return player.transform.position;
            return Camera.main ? Camera.main.transform.position : Vector3.zero;
        }
    }

    Vector3 PlayerOrDefault()
    {
        Vector3 p = PlayerPosition;
        return p != Vector3.zero ? p : transform.position + (slideDirection.sqrMagnitude > .001f ? slideDirection : Vector3.forward) * 2f;
    }

    public void Toggle(Vector3 playerPosition)
    {
        if (!opened && slide) openingDirection = Outward(playerPosition);
        opened = !opened;
    }
    public void Toggle() { Toggle(PlayerOrDefault()); }

    /// <summary>直接拉开（剧情里自动弹开抽屉用），不经过玩家点击。</summary>
    public void Open()
    {
        if (slide) openingDirection = Outward(PlayerOrDefault());
        opened = true;
    }

    void Update()
    {
        amount = Mathf.MoveTowards(amount, opened ? 1f : 0f, speed * Time.deltaTime);
        if (slide)
            transform.position = closedPosition + openingDirection * (travel * amount);
        else
            transform.SetPositionAndRotation(closedPosition, Quaternion.AngleAxis(openAngle * amount, Vector3.up) * closedRotation);
    }
}
