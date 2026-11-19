using UnityEngine;
public sealed class PickupItem : MonoBehaviour
{
    public string displayName = "道具";
    public string storyId = "";
    public string itemType = "道具";
    [Min(1)] public int quantity = 1;
    public Sprite icon;
    [TextArea] public string description = "";
}
