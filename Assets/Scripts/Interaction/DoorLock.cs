using UnityEngine;

/// <summary>
/// 门锁：
/// · 普通锁门（二号房间）：积木谜题没解开之前打不开；解开之后还要先捡到钥匙。
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
    public bool Unlocked => unlocked;
    public string LockedPrompt => unlocked ? "E 开门 / 关门" : "E 查看门锁";

    bool BlocksSolved()
    {
        if (!requireBlockPuzzle) return true;
        var puzzle = FindObjectOfType<BlockPuzzle>();
        return puzzle && puzzle.Solved;
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
            StoryProgress.Instance?.Enqueue("门锁着", reason);
            return false;
        }
        unlocked = true;
        if (!string.IsNullOrEmpty(requiredItemId)) inventory.ConsumeItem(requiredItemId);
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
