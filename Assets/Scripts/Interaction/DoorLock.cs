using UnityEngine;

/// <summary>
/// 门锁：
/// · 普通锁门（二楼二号房间）：拿到钥匙就能开；只有 requireBlockPuzzle 打开的门
///   才额外要求积木谜题先解完（一号房间的 2 号门用不到这个组件，它的顺序由 RoomDoorGate 管）。
/// · 小房间门：把房间整理干净之前打不开。
/// </summary>
[DisallowMultipleComponent]
public sealed class DoorLock : MonoBehaviour
{
    [Tooltip("谜题三（积木块谜题）没解完时，这扇门不能开。")]
    public bool requireBlockPuzzle = true;
    [Tooltip("开门需要的道具 id（空 = 不需要钥匙）。")]
    public string requiredItemId = "room2_key";
    [Tooltip("整理归位谜题没完成时，这扇门不能开（小房间门用）。")]
    public bool requireRoom2Tidy;
    [Tooltip("这扇门打开后，开始“二楼二号房间”的整理谜题。")]
    public bool startsRoom2Tidy;
    [TextArea] public string lockedMessage = "门锁着，好像现在还不到时候。";
    [TextArea] public string needKeyMessage = "门锁着，得先找到钥匙。";
    [TextArea] public string unlockMessage = "用钥匙打开了二楼二号房间的门。";
    [TextArea] public string tidyLockedMessage = "这扇门打不开，得先把房间整理干净。";

    bool unlocked;
    /// <summary>积木谜题一旦解完就永远算解开：免得某个瞬间找不到谜题对象，门又“变回锁着”。</summary>
    bool blocksSolvedOnce;

    public bool Unlocked => unlocked;
    public string LockedPrompt => unlocked ? "E 开门 / 关门" : "E 查看门锁";

    bool BlocksSolved()
    {
        if (!requireBlockPuzzle || blocksSolvedOnce) return true;
        var puzzle = FindObjectOfType<BlockPuzzle>();
        // 场景里压根没有积木谜题：这扇门不该用“积木没拼完”来拦（门序由 RoomDoorGate 管）。
        if (!puzzle) { blocksSolvedOnce = true; return true; }
        if (puzzle.Solved) blocksSolvedOnce = true;
        return blocksSolvedOnce;
    }

    bool TidySolved()
    {
        if (!requireRoom2Tidy) return true;
        return Room2Tidy.Instance && Room2Tidy.Instance.Solved;
    }

    /// <summary>还没解锁时，返回门锁状态的提示文字；已解锁返回空串。</summary>
    public string LockedReason(PlayerInventory inventory)
    {
        if (unlocked) return "";
        if (!BlocksSolved()) return lockedMessage;
        if (!TidySolved()) return tidyLockedMessage;
        if (!string.IsNullOrEmpty(requiredItemId) && (!inventory || !inventory.HasItem(requiredItemId)))
            return needKeyMessage;
        return "";
    }

    /// <summary>想开门时调用：能开就顺手消耗钥匙并返回 true。</summary>
    public bool TryUnlock(PlayerInventory inventory)
    {
        if (unlocked) return true;
        string reason = LockedReason(inventory);
        if (!string.IsNullOrEmpty(reason))
        {
            Debug.Log("[门锁] " + name + " 打不开：" + reason
                + "（积木已完成=" + BlocksSolved()
                + "，需要道具=" + (string.IsNullOrEmpty(requiredItemId) ? "无" : requiredItemId)
                + "，背包里有=" + (inventory && !string.IsNullOrEmpty(requiredItemId) ? inventory.HasItem(requiredItemId).ToString() : "—") + "）", this);
            StoryProgress.Instance?.Enqueue("门锁着", reason);
            return false;
        }
        unlocked = true;
        bool usedKey = false;
        if (!string.IsNullOrEmpty(requiredItemId)) usedKey = inventory.ConsumeItem(requiredItemId);
        Debug.Log("[门锁] " + name + " 已解锁" + (usedKey ? "（消耗了道具 " + requiredItemId + "）" : "（不需要道具）"), this);
        if (!string.IsNullOrEmpty(unlockMessage))
            StoryProgress.Instance?.Enqueue("开门", unlockMessage);
        if (startsRoom2Tidy) Room2Tidy.Instance?.Begin();
        return true;
    }

    /// <summary>剧情解锁（整理完成后自动开小房间门），不消耗道具、不弹通用提示。</summary>
    public void UnlockSilently()
    {
        unlocked = true;
    }
}
