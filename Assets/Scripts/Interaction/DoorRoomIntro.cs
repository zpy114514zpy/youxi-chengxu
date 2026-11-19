using UnityEngine;

public sealed class DoorRoomIntro : MonoBehaviour
{
    public int doorNumber = 1;
    [TextArea(2, 5)] public string introduction;
    bool shown;

    public void OnOpened()
    {
        if (shown || !StoryProgress.Instance) return;
        shown = true;
        StoryProgress.Instance.Enqueue("二楼一号房间" + doorNumber + "号门：" + introduction);
    }
}
