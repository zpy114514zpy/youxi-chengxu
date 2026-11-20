using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 凳子：可以被 Q+WASD 推动（PushProp），玩家走到旁边按 E 踩上凳面。
/// 踩上去之后角色抬高了，才够得到高处的密码箱。
///
/// 注意：角色是带重力的动力学刚体，凳子又不在 Ground 层，光“瞬移上去”会被重力立刻拽回地面。
/// 所以这里改成调用 PlayerController.StandOnPlatform —— 由玩家自己关重力、每帧钉在凳面中心，
/// 直到玩家按方向键或再按一次 E 迈下来。
/// </summary>
[RequireComponent(typeof(PushProp))]
[DisallowMultipleComponent]
public sealed class StoolStep : MonoBehaviour
{
    /// <summary>场景里所有凳子，密码箱靠它判断“凳子是不是推到箱子下面了”。</summary>
    public static readonly List<StoolStep> All = new List<StoolStep>();

    [Tooltip("离凳子多近能踩上去。")]
    public float reach = 2.2f;

    PlayerController player;
    bool onTop;
    bool restoredOnce;
    Vector3 home;

    public bool PlayerOnTop => onTop;

    public string Prompt
    {
        get
        {
            if (onTop) return "E 下凳子";
            if (PlayerNear()) return "E 踩上凳子";
            return GetComponent<PushProp>() ? "按住 Q + WASD 推动" : "";
        }
    }

    void Awake()
    {
        if (!All.Contains(this)) All.Add(this);
        home = transform.position;
    }

    void OnDestroy()
    {
        All.Remove(this);
        if (player) player.LeavePlatform();
    }

    /// <summary>密码箱用：凳子有没有被推到指定位置附近。</summary>
    public bool IsNear(Vector3 point, float radius)
    {
        Vector3 a = transform.position, b = point;
        a.y = b.y = 0f;
        return Vector3.Distance(a, b) <= radius;
    }

    public void Interact()
    {
        if (onTop) { StepDown(); return; }
        if (!Resolve()) return;
        if (!PlayerNear()) return;
        StepUp();
    }

    bool PlayerNear()
    {
        if (!Resolve()) return false;
        Vector3 a = player.transform.position, b = transform.position;
        a.y = b.y = 0f;
        return Vector3.Distance(a, b) <= reach;
    }

    bool Resolve()
    {
        if (!player) player = FindObjectOfType<PlayerController>();
        return player != null;
    }

    void StepUp()
    {
        Bounds top = TopBounds();
        Vector3 feet = new Vector3(top.center.x, top.max.y + .02f, top.center.z);
        player.StandOnPlatform(feet);
        onTop = true;
        Debug.Log("[凳子] 站上凳子：凳面 " + top.max.y.ToString("F2")
            + "，脚底落点 " + feet.ToString("F2"), this);
    }

    void StepDown()
    {
        // 朝角色正面迈下去；四个方向都被挡住就保持站着，避免卡进墙里。
        if (player.StepOff(player.transform.forward)) onTop = false;
    }

    void Update()
    {
        // 兜底：凳子万一被物理漏到地板下面（拿不到地面就会一直往下掉），直接复位。
        if (transform.position.y < home.y - .6f) { Restore(); return; }

        if (!onTop) return;
        if (!Resolve()) { onTop = false; return; }
        // 玩家自己按方向键走下来了。
        if (!player.OnPlatform) { onTop = false; return; }

        Bounds top = TopBounds();
        player.KeepOnPlatform(new Vector3(top.center.x, top.max.y + .02f, top.center.z));
    }

    void Restore()
    {
        // 注意：这里的刚体是凳子自己的，不是玩家的。
        var stoolBody = GetComponent<Rigidbody>();
        if (stoolBody)
        {
            stoolBody.velocity = Vector3.zero;
            stoolBody.angularVelocity = Vector3.zero;
            stoolBody.position = home;
        }
        else transform.position = home;
        if (player) player.LeavePlatform();
        onTop = false;
        if (restoredOnce) return;
        restoredOnce = true;
        Debug.Log("[凳子] 凳子掉出地板，已复位到 " + home.ToString("F2"), this);
    }

    Bounds TopBounds()
    {
        var renderers = GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            var collider = GetComponent<Collider>();
            return collider ? collider.bounds : new Bounds(transform.position, Vector3.one * .3f);
        }
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }
}
