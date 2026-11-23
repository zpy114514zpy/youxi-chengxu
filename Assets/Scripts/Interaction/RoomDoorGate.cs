using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 剧情房间门的闸门，分两段：
///   ① 第一关（卧室书桌玩偶谜题）没完成之前，除了大门（白雾那扇）以外的房门都不允许打开，
///      免得玩家提前跑进别的房间、把剧情顺序打乱；谜题一完成、书桌中间抽屉弹开的同一刻放开。
///   ② 放开之后，二楼一号房间的二号门（BP_Door_Inside_A6）仍然锁着 —— 它通往第三个解密任务，
///      要等积木谜题做完、UI 提示“前往二号门”的那一刻才由 UnlockDoorTwo() 打开。
/// </summary>
public static class RoomDoorGate
{
    static bool unlocked;
    static bool doorTwoLocked;

    public static bool Unlocked => unlocked;

    /// <summary>提示文案：还没解锁时，准星上显示的是“查看”而不是“开门”。</summary>
    public const string Prompt = "E 查看房门";
    public const string Message = "现在还不能去别的房间。先把书桌上那件东西弄完吧。";
    public const string DoorTwoMessage = "这扇门还打不开。先把五块积木全部搬回积木堆吧。";

    /// <summary>第一关完成：放开其他房门，但二号门继续锁着。</summary>
    public static void Unlock()
    {
        unlocked = true;
        doorTwoLocked = true;
    }

    /// <summary>积木谜题完成、UI 提示“前往二楼一号房间2号门”的那一刻调用。</summary>
    public static void UnlockDoorTwo() => doorTwoLocked = false;

    /// <summary>这扇门现在是不是还被剧情锁着（出口大门不算，那扇一直是白雾）。</summary>
    public static bool Blocks(InteractiveDoor door)
    {
        if (!door) return false;
        if (door.GetComponentInParent<ExitBoundary>()) return false;
        if (!unlocked) return true;
        return doorTwoLocked && IsDoorTwo(door);
    }

    /// <summary>
    /// 是不是“二楼一号房间二号门”。优先用门上的 DoorRoomIntro.doorNumber（场景里 A5=1、A6=2、A4=3），
    /// 没配编号时退回看物体名里的 A6。
    /// </summary>
    static bool IsDoorTwo(InteractiveDoor door)
    {
        if (!door) return false;
        var intro = door.GetComponent<DoorRoomIntro>();
        if (!intro) intro = door.GetComponentInParent<DoorRoomIntro>();
        if (intro) return intro.doorNumber == 2;
        for (Transform t = door.transform; t; t = t.parent)
            if (t.name.IndexOf("A6", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
        return false;
    }

    public static void Deny()
    {
        StoryProgress.Instance?.Enqueue("房门", Message);
    }

    public static void Deny(InteractiveDoor door)
    {
        // 第一关还没做完时所有门都是同一个理由；做完之后只有二号门还锁着。
        bool stageTwo = unlocked && IsDoorTwo(door);
        StoryProgress.Instance?.Enqueue("房门", stageTwo ? DoorTwoMessage : Message);
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
        // 第二关的门全部常开：这道闸门只服务于第一关的剧情顺序。
        if (scene.name == "Level 2") { unlocked = true; doorTwoLocked = false; return; }
        unlocked = false;
        doorTwoLocked = false;
    }
}
