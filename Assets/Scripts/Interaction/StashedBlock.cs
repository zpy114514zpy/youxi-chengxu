using UnityEngine;

/// <summary>
/// 抽屉里那块积木的“藏 / 露”开关，外加一条兜底：保证玩家真的看得见、点得到。
///
/// 为什么不能直接把它塞进抽屉就完事：
///   · 床头柜的抽屉网格常常只是那块面板，积木塞进去就被柜板包住 —— 描边、模型都看不见；
///   · 被包住的同时射线也先撞到柜板，准星永远显示“打开 / 关闭抽屉”，玩家根本点不到积木。
/// 所以抽屉拉开之后，这里会从玩家相机往积木打一条射线：先撞到的若不是积木，就顺着
/// 抽屉开口方向把它一点点挪出来，直到能看见为止（最多挪 34cm，超过就不再折腾）。
/// 抽屉关着的时候仍然只关渲染器和碰撞体：看不见、也捡不到，必须先把抽屉拉开。
/// </summary>
[DisallowMultipleComponent]
public sealed class StashedBlock : MonoBehaviour
{
    FurnitureOpenable drawer;
    GameObject content;
    Renderer[] renderers;
    Collider[] colliders;
    ProximityOutline outline;
    PlayerController player;

    bool stashed = true;
    bool verified;
    bool released;

    /// <summary>已经被玩家取走 / 丢出来了：从此刻起永久显示。</summary>
    public bool Released => released;

    public void Setup(FurnitureOpenable source, GameObject item)
    {
        drawer = source;
        content = item;
        if (!content) { stashed = false; return; }
        renderers = content.GetComponentsInChildren<Renderer>(true);
        colliders = content.GetComponentsInChildren<Collider>(true);
        EnsureOutline();
        Show(false);
    }

    /// <summary>
    /// 被玩家捡走（或者丢到地上）时调用：从此永久显示，不再跟着抽屉的开关走。
    /// 少了这一步，积木从抽屉里拿出来后会是一块“隐形砖头” —— 渲染器还是关着的。
    /// </summary>
    public void Release()
    {
        released = true;
        stashed = false;
        verified = true;
        Show(true);
        // 出了抽屉就回到积木堆上其他积木那套描边，别留一颗蓝不蓝灰不灰的。
        RestoreDefaultOutline();
    }

    void Update()
    {
        if (released || !stashed) return;
        if (!content || !drawer) { stashed = false; return; }
        // 已经被玩家捡走（SetActive(false)），或者已经不是抽屉的子物体了：不再插手。
        if (!content.activeSelf || !content.transform.IsChildOf(transform)) { stashed = false; return; }

        bool open = drawer.OpenAmount > .35f;
        Show(open);
        if (open && !verified) Verify();
    }

    void Show(bool visible)
    {
        if (renderers != null)
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i]) renderers[i].enabled = visible;
        if (colliders != null)
            for (int i = 0; i < colliders.Length; i++)
                if (colliders[i]) colliders[i].enabled = visible;
    }

    /// <summary>
    /// 只给一圈描边，不打灯：抽屉是半封闭的小盒子，点光一放进去就会把内壁糊成白块，
    /// 积木本身反而看不出来。颜色跟积木堆上其他积木一致（青蓝），但更淡一点，
    /// 因为这块是“藏在抽屉里的”，不该比明面上的积木更抢眼。
    /// </summary>
    void EnsureOutline()
    {
        if (!content) return;

        outline = content.GetComponent<ProximityOutline>();
        if (!outline) outline = content.AddComponent<ProximityOutline>();
        outline.Apply(new Color(.28f, .82f, 1f, .55f), .008f, 2.8f);   // 站到床头柜前面才亮
    }

    /// <summary>被拿出抽屉后恢复成积木堆上其他积木的描边参数。</summary>
    void RestoreDefaultOutline()
    {
        if (!outline) outline = content ? content.GetComponent<ProximityOutline>() : null;
        if (!outline) return;
        outline.Apply(new Color(.25f, .9f, 1f, .95f), .006f, 2.6f);
    }

    /// <summary>从玩家相机往回看：积木被柜板挡住了就把它往抽屉口推，直到真的能看见。</summary>
    void Verify()
    {
        var camera = Camera.main;
        if (!camera) return;
        if (!player) player = FindObjectOfType<PlayerController>();
        if (!player) return;

        // 只有玩家在附近、而且大致朝着这个方向时才做判断，
        // 否则隔着墙打出来的“看不见”会白白把积木推出去。
        Vector3 toBlock = Center() - player.transform.position;
        if (toBlock.sqrMagnitude > 16f) return;
        if (Vector3.Dot(camera.transform.forward, toBlock.normalized) < .25f) return;

        // 只校准一次：不管最后有没有露出来都不再重复推，免得积木越跑越远。
        Visible();
        verified = true;
    }

    /// <summary>没被挡住（或挪了几步之后看见了）才算完成。</summary>
    bool Visible()
    {
        Vector3 direction = Vector3.ProjectOnPlane(drawer.OpeningDirection, Vector3.up);
        if (direction.sqrMagnitude < .0004f) direction = content.transform.forward;
        direction.Normalize();

        for (int step = 0; step < 6; step++)
        {
            if (Seen()) return true;
            content.transform.position += direction * .03f;
            Physics.SyncTransforms();
        }
        Debug.Log("[积木谜题] 抽屉里的积木一直被柜板挡住，已推到抽屉口（" + content.transform.position.ToString("F2") + "）。", this);
        return false;
    }

    /// <summary>相机到积木中心这条线上，第一个撞到的可交互物是不是积木自己。</summary>
    bool Seen()
    {
        var camera = Camera.main;
        if (!camera) return true;
        Vector3 target = Center();
        Vector3 origin = camera.transform.position;
        Vector3 delta = target - origin;
        float distance = delta.magnitude;
        if (distance < .06f) return true;

        foreach (var hit in Physics.RaycastAll(origin, delta / distance, distance + .04f,
                     Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (!hit.collider) continue;
            var hitTransform = hit.collider.transform;
            // 注意：碰撞体就在积木自己身上时，IsChildOf 是 false，必须单独判一次。
            if (hitTransform == content.transform || hitTransform.IsChildOf(content.transform)) return true;
            if (player && (hitTransform == player.transform || hitTransform.IsChildOf(player.transform))) continue;
            if (hit.collider.isTrigger) continue;
            return false;      // 先撞到柜板 / 床头柜 / 墙：积木在里面，看不见
        }
        return true;
    }

    Vector3 Center()
    {
        if (!content) return transform.position;
        var found = content.GetComponentsInChildren<Renderer>(true);
        if (found.Length == 0) return content.transform.position;
        Bounds bounds = found[0].bounds;
        for (int i = 1; i < found.Length; i++)
            if (found[i]) bounds.Encapsulate(found[i].bounds);
        return bounds.center;
    }

    void OnDestroy()
    {
        if (content) Show(true);      // 兜底：别在场景里留下一个看不见的积木
    }
}
