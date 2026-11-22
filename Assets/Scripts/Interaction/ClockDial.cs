using UnityEngine;

/// <summary>
/// 客厅墙上的老式挂钟：每按一次 E 把指针往前拨 30 分钟（时针跟着分针走）。
/// 拨到 03:30 就解开，抽屉由 <see cref="LivingRoomClockPuzzle"/> 负责弹开。
/// </summary>
public sealed class ClockDial : MonoBehaviour
{
    public Transform hourHand;
    public Transform minuteHand;

    public int targetHour = 3;
    public int targetMinute = 30;

    public int hour = 12;
    public int minute;

    public bool solved;
    public System.Action onSolved;

    Quaternion hourBase, minuteBase;
    bool baseReady;
    float cooldown;

    public string TimeText => hour.ToString("00") + ":" + minute.ToString("00");

    public string Prompt => solved
        ? "时钟已停在 " + TimeText
        : "E 调整时钟（当前 " + TimeText + "）";

    void Awake() { CacheBase(); }

    /// <summary>两只针的初始朝向就是 12:00，记下来当基准。</summary>
    void CacheBase()
    {
        if (baseReady) return;
        if (hourHand) hourBase = hourHand.rotation;
        if (minuteHand) minuteBase = minuteHand.rotation;
        baseReady = hourHand || minuteHand;
    }

    public void Tick()
    {
        if (solved || Time.time < cooldown) return;
        cooldown = Time.time + .16f;

        minute += 30;
        if (minute >= 60)
        {
            minute = 0;
            hour++;
            if (hour > 12) hour = 1;
        }
        Apply();

        if (hour == targetHour && minute == targetMinute)
        {
            solved = true;
            onSolved?.Invoke();
        }
    }

    void Apply()
    {
        CacheBase();
        // 钟面法线是世界 +Z 的反向（房间在 -Z 一侧），绕 +Z 正向旋转看起来就是顺时针。
        float h = (hour % 12) * 30f + minute * .5f;
        float m = minute * 6f;
        if (hourHand) hourHand.rotation = Quaternion.AngleAxis(h, Vector3.forward) * hourBase;
        if (minuteHand) minuteHand.rotation = Quaternion.AngleAxis(m, Vector3.forward) * minuteBase;
    }

    /// <summary>直接把时钟设到某个时刻（调试 / 剧情用）。拨到目标时刻同样会触发解法流程。</summary>
    public void ForceTime(int h, int m)
    {
        hour = h; minute = m; Apply();
        if (hour != targetHour || minute != targetMinute || solved) return;
        solved = true;
        onSolved?.Invoke();
    }
}
