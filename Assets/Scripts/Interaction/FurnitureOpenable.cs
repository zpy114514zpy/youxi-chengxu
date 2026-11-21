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
        slideDirection = slideDirection.sqrMagnitude < .001f ? Vector3.forward : slideDirection.normalized;
        openingDirection = slideDirection;
    }

    /// <summary>
    /// 抽屉/柜门的“朝外”方向。
    /// 不能简单用“柜体中心 → 抽屉中心”，因为一排柜子有两列抽屉时那个方向是横向的，抽屉会左右乱窜。
    /// 正确做法：先取出柜面的法线（面板最薄的那根轴），再让它朝向玩家 —— 玩家永远站在柜子前面，
    /// 所以“朝玩家”就是“往外拉”。
    /// </summary>
    Vector3 Outward(Vector3 playerPosition)
    {
        var visual = GetComponent<Renderer>();
        Vector3 center = visual ? visual.bounds.center : transform.position;
        Vector3 axis = FrontAxis(visual);

        Vector3 towardPlayer = Vector3.ProjectOnPlane(playerPosition - center, Vector3.up);
        if (towardPlayer.sqrMagnitude < .0004f) return axis;
        return axis * (Vector3.Dot(axis, towardPlayer) >= 0f ? 1f : -1f);
    }

    /// <summary>柜面法线：优先用本面板最薄的水平轴；抽屉盒（最薄的是高度）就看同一件家具的其他面板投票。</summary>
    Vector3 FrontAxis(Renderer visual)
    {
        Vector3 axis = ThinHorizontalAxis(visual ? visual.bounds : new Bounds(transform.position, Vector3.one * .3f));
        if (axis != Vector3.zero) return axis;

        // 投票：附近 1.6m 内、同一个柜子的其他面板，谁的水平最薄轴一致就听谁的
        int right = 0, forward = 0;
        Vector3 center = visual ? visual.bounds.center : transform.position;
        foreach (var other in FindObjectsOfType<FurnitureOpenable>())
        {
            if (other == this || !other) continue;
            var r = other.GetComponent<Renderer>();
            if (!r) continue;
            Vector3 c = r.bounds.center;
            if (Mathf.Abs(c.y - center.y) > .9f) continue;
            if (Vector2.Distance(new Vector2(c.x, c.z), new Vector2(center.x, center.z)) > 1.6f) continue;
            Vector3 a = ThinHorizontalAxis(r.bounds);
            if (a == Vector3.right) right++;
            else if (a == Vector3.forward) forward++;
        }
        if (right == 0 && forward == 0)
        {
            // 兜底：柜体中心 → 面板中心的水平方向（单列抽屉的桌子用这个就够）
            Vector3 bodyCenter = BodyCenter(visual);
            Vector3 fromBody = Vector3.ProjectOnPlane(center - bodyCenter, Vector3.up);
            if (fromBody.sqrMagnitude > .0004f)
            {
                Vector3 d = fromBody.normalized;
                return Mathf.Abs(d.x) >= Mathf.Abs(d.z) ? Vector3.right : Vector3.forward;
            }
            return slideDirection.sqrMagnitude > .001f ? slideDirection : Vector3.forward;
        }
        return right > forward ? Vector3.right : Vector3.forward;
    }

    /// <summary>柜体轴心：往上找第一个明显比自己大的渲染器（抽屉自己的父节点常常只是个空壳，位置是偏的）。</summary>
    Vector3 BodyCenter(Renderer visual)
    {
        Vector3 size = visual ? visual.bounds.size : Vector3.one * .3f;
        for (var t = transform.parent; t; t = t.parent)
        {
            var r = t.GetComponent<Renderer>();
            if (r && r.bounds.size.sqrMagnitude > size.sqrMagnitude * 4f) return r.bounds.center;
        }
        return transform.parent ? transform.parent.position : transform.position;
    }

    /// <summary>最薄的那根轴；最薄的是竖直轴时返回 zero（说明这是个抽屉盒，不是面板）。</summary>
    static Vector3 ThinHorizontalAxis(Bounds b)
    {
        Vector3 s = b.size;
        if (s.y <= s.x && s.y <= s.z) return Vector3.zero;
        if (s.x <= s.z) return Vector3.right;
        return Vector3.forward;
    }

    static Vector3 PlayerPosition => Camera.main ? Camera.main.transform.position : Vector3.zero;

    Vector3 PlayerOrModelFront()
    {
        Vector3 p = PlayerPosition;
        return p != Vector3.zero ? p : transform.position + (slideDirection.sqrMagnitude > .001f ? slideDirection : Vector3.forward) * 2f;
    }

    public void Toggle(Vector3 playerPosition)
    {
        if (!opened && slide) openingDirection = Outward(playerPosition);
        opened = !opened;
    }
    public void Toggle() { Toggle(PlayerOrModelFront()); }

    /// <summary>直接拉开（剧情里自动弹开抽屉用），不经过玩家。</summary>
    public void Open()
    {
        if (slide) openingDirection = Outward(PlayerOrModelFront());
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
