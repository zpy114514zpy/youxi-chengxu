using System.Collections.Generic;
using System.Collections;
using UnityEngine;

/// <summary>
/// 第二关的房门。
/// 场景里那些 BP_Door_Inside_* 在第一关是挂 InteractiveDoor 的，第二关没有，
/// 结果门板连碰撞带网面杵在门框里、按 E 毫无反应 —— 玩家会被关死在二号房。
/// 这里给每扇内门补一个最朴素的开关：按 E 推开，门板绕合页转 92°，门洞就通了。
/// 出去外面那扇（A3，正对出生点）要等谜题一修补完全家福才放行。
///
/// 推的方向按玩家站的位置现算：门板永远往“远离玩家的那一侧”转，
/// 所以站在门外按 E 是往里推、站在门里按 E 是往外推 —— 一号小房间那扇门也就不会再“拉”开。
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

/// <summary>一扇能推开的门。组件挂在门板根节点上，射线打到门板的 MeshCollider 就能找到它。</summary>
public sealed class Level2Door : L2Interactable
{
    public Transform leaf;
    public bool locked;
    public string lockedHint = "这扇门还打不开。先把墙上那幅全家福补起来吧。";

    bool open;
    public bool Open => open;
    public string Key => leaf ? leaf.name : name;

    public override string Prompt
    {
        get
        {
            if (open) return "";                                  // 开着就不再提示，门保持敞开
            return locked ? "E 这扇门还打不开" : "E 推开房门";
        }
    }

    public override void Interact(PlayerInventory inventory)
    {
        if (open) return;
        if (locked)
        {
            Level2Director.Instance?.Ui?.Say("房门", lockedHint);
            return;
        }
        var player = Level2Director.Instance ? Level2Director.Instance.Player : null;
        PushOpen(player ? player.position : (Vector3?)null);
    }

    public void PushOpen() => PushOpen(null);

    public void PushOpen(Vector3? from)
    {
        if (open || !leaf) return;
        open = true;
        float sign = from.HasValue ? SwingSign(from.Value) : 1f;
        StartCoroutine(Swing(sign));
    }

    /// <summary>读档：门直接保持敞开，不播动画。</summary>
    public void RestoreOpen()
    {
        if (open || !leaf) return;
        open = true;
        leaf.localRotation = leaf.localRotation * Quaternion.Euler(0f, 92f, 0f);
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

    IEnumerator Swing(float sign)
    {
        Quaternion from = leaf.localRotation;
        Quaternion to = from * Quaternion.Euler(0f, 92f * sign, 0f);
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * 2.6f;
            leaf.localRotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t));
            yield return null;
        }
        leaf.localRotation = to;
        Debug.Log($"[第二关] 门已推开 {Key}（{(sign > 0 ? "正向" : "反向")}）");
    }
}
