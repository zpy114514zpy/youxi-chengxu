using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 第一关（卧室书桌玩偶谜题）没完成之前，除了大门（白雾那扇）以外的房门都不允许打开，
/// 免得玩家提前跑进别的房间、把剧情顺序打乱。
/// 谜题一完成、书桌中间抽屉弹开的同时由 DrawerFragmentReveal 调用 Unlock() 放开。
/// </summary>
public static class RoomDoorGate
{
    static bool unlocked;
    public static bool Unlocked => unlocked;

    /// <summary>提示文案：还没解锁时，准星上显示的是“查看”而不是“开门”。</summary>
    public const string Prompt = "E 查看房门";
    public const string Message = "现在还不能去别的房间。先把书桌上那件东西弄完吧。";

    public static void Unlock() => unlocked = true;

    /// <summary>这扇门现在是不是还被剧情锁着（出口大门不算，那扇一直是白雾）。</summary>
    public static bool Blocks(InteractiveDoor door)
    {
        if (unlocked || !door) return false;
        return !door.GetComponentInParent<ExitBoundary>();
    }

    public static void Deny()
    {
        StoryProgress.Instance?.Enqueue("房门", Message);
    }

    // 重新加载场景（重开一局）时把开关复位，不然第二次进去门会一直是开的。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        unlocked = false;
    }
}
