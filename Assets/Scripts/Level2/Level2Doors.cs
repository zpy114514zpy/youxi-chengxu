using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 第二关的房门。
/// 场景里那些 BP_Door_Inside_* 在第一关是挂 InteractiveDoor 的，第二关没有，
/// 结果门板连碰撞带网面杵在门框里、按 E 毫无反应 —— 玩家会被关死在二号房。
/// 这里给每扇内门补一个最朴素的开关：按 E 开 / 关，门板绕合页转 92°，门洞就通了。
/// 开门的方向按玩家站的位置现算（永远往远离玩家的那一侧转），关门沿原路转回去。
/// 出去外面那扇（A3，正对出生点）要等谜题一修补完全家福才放行。
/// </summary>
public sealed class Level2Doors : MonoBehaviour
{
    readonly List<Level2Door> doors = new List<Level2Door>();
    public IReadOnlyList<Level2Door> All => doors;

    public void Setup()
    {
        foreach (var t in FindObjectsOfType<Transform>(true))
        {
            if (t.name.IndexOf("BP_Door_Inside", System.StringComparison.OrdinalIgnoreCase) != 0) continue;
            if (t.GetComponent<Level2Door>()) continue;      // 重进 Play 不重复装配

            var door = t.gameObject.AddComponent<Level2Door>();
            door.leaf = t;
            // 出去外面那扇：A3，门在二号房北墙、正对出生点。
            door.locked = t.name.EndsWith("A3");
            doors.Add(door);
        }
        Debug.Log($"[第二关] 房门 {doors.Count} 扇，其中上锁 {CountLocked()}");
    }

    int CountLocked()
    {
        int n = 0;
        foreach (var d in doors) if (d.locked) n++;
        return n;
    }

    /// <summary>某扇门（按名字里的一段）是不是已经开了。</summary>
    public bool IsOpen(string keyFragment)
    {
        if (string.IsNullOrEmpty(keyFragment)) return false;
        foreach (var d in doors)
            if (d && d.Key != null && d.Key.IndexOf(keyFragment, System.StringComparison.OrdinalIgnoreCase) >= 0 && d.Open)
                return true;
        return false;
    }

    /// <summary>
    /// 谜题一完成：只把出口门解锁，不替玩家推门。
    /// 以前这里顺手 PushOpen 了一下，玩家补完全家福回头就看见门自己敞着，
    /// 像是场景里本来就开着 —— 现在门保持关着，等玩家自己走过去按 E。
    /// </summary>
    public void OpenExit()
    {
        foreach (var d in doors)
        {
            if (!d.locked) continue;
            d.locked = false;
            Debug.Log($"[第二关] 出口门已解锁 {d.Key}（等玩家自己推开）");
        }
    }

    // ---------------- 存档 ----------------

    public string CaptureState()
    {
        var list = new List<string>();
        foreach (var d in doors) if (d && d.Open) list.Add(d.Key);
        return string.Join(",", list);
    }

    public void RestoreState(string state)
    {
        if (string.IsNullOrEmpty(state)) return;
        foreach (var key in state.Split(','))
        {
            if (string.IsNullOrEmpty(key)) continue;
            foreach (var d in doors)
                if (d && d.Key == key) d.RestoreOpen();
        }
    }
}

/// <summary>
/// 一扇能开、也能关的门。组件挂在门板根节点上，射线打到门板的 MeshCollider 就能找到它。
/// 开的方向按玩家站的位置现算（往远离玩家的那一侧转），关门就沿原路转回去；
/// 开 / 关各响一声，用的就是第一关那套门声（GameAudio.PlayDoor）。
/// </summary>
public sealed class Level2Door : L2Interactable
{
    public Transform leaf;
    public bool locked;
    public string lockedHint = "这扇门还打不开。先把墙上那幅全家福补起来吧。";

    const float OpenAngle = 92f;
    const float SwingSpeed = 300f;        // 度/秒，和第一关的门手感接近

    Quaternion baseRot = Quaternion.identity;   // 关上时门板的本地旋转
    bool hasBase;
    float angle, target, sign = 1f;
    bool open;

    public bool Open => open;
    public string Key => leaf ? leaf.name : name;

    public override string Prompt
    {
        get
        {
            if (open) return "E 关上门";
            return locked ? "E 这扇门还打不开" : "E 推开房门";
        }
    }

    void Awake() => EnsureBase();

    void EnsureBase()
    {
        if (hasBase || !leaf) return;
        baseRot = leaf.localRotation;          // AddComponent 时门还关着，这时记下来的就是“关”的姿态
        hasBase = true;
    }

    public override void Interact(PlayerInventory inventory)
    {
        if (!open && locked)
        {
            Level2Director.Instance?.Ui?.Say("房门", lockedHint);
            return;
        }
        var player = Level2Director.Instance ? Level2Director.Instance.Player : null;
        Toggle(player ? player.position : (Vector3?)null);
    }

    /// <summary>开 / 关一次。音效与第一关一致：开门原声，关门音高压低一点。</summary>
    public void Toggle(Vector3? from)
    {
        if (!leaf) return;
        EnsureBase();

        if (open)                                  // 已经开着 → 关
        {
            open = false;
            target = 0f;
            GameAudio.PlayDoor(.88f, .80f);
            Debug.Log($"[第二关] 关门 {Key}");
            return;
        }

        open = true;                               // 关着 → 开
        sign = from.HasValue ? SwingSign(from.Value) : 1f;
        target = OpenAngle * sign;
        GameAudio.PlayDoor(1f, .85f);
        Debug.Log($"[第二关] 开门 {Key}（{(sign > 0 ? "正向" : "反向")}）");
    }

    void Update()
    {
        if (!leaf || Mathf.Approximately(angle, target)) return;
        EnsureBase();
        angle = Mathf.MoveTowards(angle, target, SwingSpeed * Time.deltaTime);
        leaf.localRotation = baseRot * Quaternion.Euler(0f, angle, 0f);
    }

    public void PushOpen() => Toggle(null);
    public void PushOpen(Vector3? from) => Toggle(from);

    /// <summary>读档：门直接保持敞开，不播动画。</summary>
    public void RestoreOpen()
    {
        if (open || !leaf) return;
        EnsureBase();
        open = true;
        sign = 1f;
        angle = target = OpenAngle;
        leaf.localRotation = baseRot * Quaternion.Euler(0f, angle, 0f);
    }

    /// <summary>
    /// 门往哪边转：把门板上离合页最远的一点分别转 +92° / -92°，
    /// 取转完之后离玩家更远的那一边 —— 门永远是“被推开”，不是“被拉开”。
    /// </summary>
    float SwingSign(Vector3 player)
    {
        var b = L2.BoundsOf(leaf);
        // 门板中心在门板本地空间里的位置，再往外推到远端的门边。
        Vector3 localCenter = leaf.InverseTransformPoint(b.center);
        Vector3 probe = localCenter + Vector3.right * (b.extents.magnitude * .55f);

        Vector3 a = leaf.position + (leaf.rotation * Quaternion.Euler(0f, 92f, 0f)) * probe;
        Vector3 c = leaf.position + (leaf.rotation * Quaternion.Euler(0f, -92f, 0f)) * probe;
        return Vector3.Distance(a, player) >= Vector3.Distance(c, player) ? 1f : -1f;
    }
}
