using UnityEngine;

// Place on an individual cabinet leaf or drawer, not on the entire cabinet.
public sealed class FurnitureOpenable : MonoBehaviour
{
    /// <summary>true = 抽屉，直着朝外拉出；false = 柜门，绕侧边铰链平开。</summary>
    public bool slide = true;
    /// <summary>平开门：铰链在右侧（否则在左侧）。</summary>
    public bool hingeOnRight;
    public Vector3 slideDirection = Vector3.forward;
    public float travel = .30f;
    public float openAngle = 88f;
    public float speed = 2.6f;

    /// <summary>剧情锁：true 时玩家按 E 拉不开（剧情调用 Open() 不受影响）。</summary>
    [System.NonSerialized] public bool locked;
    /// <summary>拉不开时弹给玩家的一句话。</summary>
    [System.NonSerialized] public string lockedHint = "";

    public bool Locked => locked;

    /// <summary>玩家手动开合时用：锁着就弹提示，不动。</summary>
    public void LockByStory(string hint)
    {
        locked = true;
        lockedHint = hint;
    }

    public void UnlockByStory()
    {
        locked = false;
        lockedHint = "";
    }

    static readonly Vector3[] Axes =
    {
        Vector3.right, Vector3.left, Vector3.forward, Vector3.back
    };

    Vector3 closedPosition;
    Quaternion closedRotation;
    float amount;
    bool opened;
    Vector3 openingDirection = Vector3.forward;
    Vector3 hingePoint;
    float hingeSign = 1f;

    public string Prompt => locked
        ? (slide ? "E 拉一下抽屉" : "E 拉一下柜门")
        : (slide ? "E 打开 / 关闭抽屉" : "E 打开 / 关闭柜门");

    void Awake()
    {
        closedPosition = transform.position;
        closedRotation = transform.rotation;
        if (slideDirection.sqrMagnitude < .001f) slideDirection = Vector3.forward;
        slideDirection.Normalize();
        openingDirection = slideDirection;
    }

    Renderer Visual()
    {
        var r = GetComponent<Renderer>();
        return r ? r : GetComponentInChildren<Renderer>();
    }

    /// <summary>面板的视觉中心。抽屉模型的 pivot 常常全压在家具原点上，不能用 transform.position。</summary>
    Vector3 Center()
    {
        var visual = Visual();
        return visual ? visual.bounds.center : transform.position;
    }

    public Bounds PanelBounds()
    {
        var visual = Visual();
        return visual ? visual.bounds : new Bounds(transform.position, Vector3.one * .2f);
    }

    static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

    /// <summary>
    /// 沿 dir 量“能走多远”。柜体、墙、别的家具拦住的越近，越不可能是开口方向。
    /// 这是判断开口最可靠的依据——比猜哪个轴更短靠谱得多。
    /// </summary>
    float Clearance(Vector3 origin, Vector3 dir)
    {
        const float max = 1.6f;
        var hits = Physics.RaycastAll(origin + dir * .05f, dir, max, ~0, QueryTriggerInteraction.Ignore);
        float best = max;
        foreach (var h in hits)
        {
            if (!h.collider) continue;
            var t = h.collider.transform;
            if (t == transform || t.IsChildOf(transform)) continue;
            if (h.distance > .02f && h.distance < best) best = h.distance;
        }
        return best;
    }

    /// <summary>
    /// 面板开口的那根轴（无正负）：水平两轴里较短的那条。
    /// 门板的“短”是厚度（法线），抽屉盒的“短”是进深 —— 两者都对得上。
    /// 之前用“家具整体包围盒”去推，遇到并排柜体或朝向不同的桌子就会推成横向，这是错的。
    /// </summary>
    public Vector3 PanelAxis()
    {
        Vector3 s = PanelBounds().size;
        return s.x <= s.z ? Vector3.right : Vector3.forward;
    }

    /// <summary>开口方向：先定轴（较短水平轴），再按“朝玩家 / 前面更空”定正负。</summary>
    public Vector3 PickOutward(Vector3 playerPosition)
    {
        Vector3 axis = PanelAxis();
        Vector3 center = Center();

        Vector3 toPlayer = Vector3.ProjectOnPlane(playerPosition - center, Vector3.up);
        if (toPlayer.sqrMagnitude > .04f)
        {
            toPlayer.Normalize();
            // 玩家大致在正面时，直接用“朝玩家”，最符合直觉
            if (Mathf.Abs(Vector3.Dot(axis, toPlayer)) > .25f)
                return axis * (Vector3.Dot(axis, toPlayer) >= 0f ? 1f : -1f);
        }

        // 玩家站在侧面或位置未知：看哪一侧更空（家具正面朝房间、背面贴柜体/墙）
        float pos = Clearance(center, axis);
        float neg = Clearance(center, -axis);
        return pos >= neg ? axis : -axis;
    }

    /// <summary>不依赖玩家位置的开口方向（剧情自动打开时用）。</summary>
    public Vector3 PickOutwardByClearance()
    {
        Vector3 axis = PanelAxis();
        Vector3 center = Center();
        float pos = Clearance(center, axis);
        float neg = Clearance(center, -axis);
        return pos >= neg ? axis : -axis;
    }

    void PrepareHinge(Vector3 playerPosition)
    {
        Vector3 normal = PickOutward(playerPosition);
        openingDirection = normal;
        Vector3 right = Vector3.Cross(Vector3.up, normal).normalized;
        float half = Mathf.Abs(Vector3.Dot(PanelBounds().size, Abs(right))) * .5f;
        hingePoint = Center() + right * (hingeOnRight ? half : -half);
        hingeSign = hingeOnRight ? 1f : -1f;
    }

    public void Toggle(Vector3 playerPosition)
    {
        if (locked)
        {
            if (!string.IsNullOrEmpty(lockedHint))
                StoryProgress.Instance?.Enqueue("抽屉锁着", lockedHint);
            return;
        }
        if (!opened)
        {
            if (slide) openingDirection = PickOutward(playerPosition);
            else PrepareHinge(playerPosition);
        }
        opened = !opened;
    }

    public void Toggle() { Toggle(PlayerPosition); }

    /// <summary>直接打开（剧情里自动弹开抽屉用），不经过玩家点击，方向按通空度判定。</summary>
    public void Open()
    {
        if (!opened)
        {
            if (slide) openingDirection = PickOutwardByClearance();
            else
            {
                Vector3 normal = PickOutwardByClearance();
                openingDirection = normal;
                Vector3 right = Vector3.Cross(Vector3.up, normal).normalized;
                float half = Mathf.Abs(Vector3.Dot(PanelBounds().size, Abs(right))) * .5f;
                hingePoint = Center() + right * (hingeOnRight ? half : -half);
                hingeSign = hingeOnRight ? 1f : -1f;
            }
        }
        opened = true;
    }

    /// <summary>剧情开抽屉时等它滑到位用：返回 0~1 的开门进度。</summary>
    public float OpenAmount => amount;

    /// <summary>
    /// 剧情用：按调用方算好的方向打开（动画仍然走 Update）。
    /// 抽屉的“正面”有时很难从模型尺寸推出来，剧情这条路径直接用几何量出来的方向，
    /// 比 PickOutward 里“水平较短轴 = 进深”的假设稳。
    /// </summary>
    public void OpenAlong(Vector3 direction)
    {
        Vector3 dir = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (dir.sqrMagnitude > .0004f) openingDirection = dir.normalized;
        else if (slide) openingDirection = PickOutwardByClearance();
        opened = true;
    }

    /// <summary>本次打开的朝向（世界方向）。</summary>
    public Vector3 OpeningDirection => openingDirection;

    public void Close()
    {
        opened = false;
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

    void Update()
    {
        amount = Mathf.MoveTowards(amount, opened ? 1f : 0f, speed * Time.deltaTime);

        if (slide)
        {
            transform.position = closedPosition + openingDirection * (travel * amount);
            return;
        }

        float angle = hingeSign * openAngle * amount;
        var rot = Quaternion.AngleAxis(angle, Vector3.up);
        transform.rotation = rot * closedRotation;
        transform.position = hingePoint + rot * (closedPosition - hingePoint);
    }
}
