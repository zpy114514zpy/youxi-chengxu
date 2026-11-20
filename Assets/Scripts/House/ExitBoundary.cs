using UnityEngine;

// The front door is a boundary of the time gap, not an exit to the street.
public sealed class ExitBoundary : MonoBehaviour
{
    bool explained;
    public void Deny()
    {
        if(explained)return;
        explained=true;
        StoryProgress.Instance?.Enqueue("未到来的未来","门外只有朦胧的白雾。现在还不能离开这栋房子。");
    }
}
