using UnityEngine;

/// <summary>归位谜题的放置点：拿着对应道具走到这里按 E 放下。</summary>
public sealed class TidySpot : MonoBehaviour
{
    public Room2Tidy puzzle;
    public string itemId;
    public string label = "物品";

    public string Prompt
    {
        get
        {
            if (!puzzle || puzzle.IsPlaced(itemId)) return "";
            return "E 摆放 " + label;
        }
    }

    public bool Place(PlayerInventory inventory)
    {
        if (!puzzle || puzzle.IsPlaced(itemId)) return false;
        bool ok = puzzle.PlaceFromSpot(itemId, inventory);
        if (!ok)
        {
            StoryProgress.Instance?.Enqueue("提示", "请先把" + label + "拿在手上，再到这里按 E。");
            return false;
        }
        var collider = GetComponent<Collider>();
        if (collider) collider.enabled = false;
        return true;
    }
}
