using UnityEngine;

public sealed class DoorRoomIntro : MonoBehaviour
{
    public int doorNumber = 1;
    [TextArea(2, 5)] public string introduction;
    public void OnOpened()
    {
        if (!StoryProgress.Instance) return;
        string text=doorNumber==1
            ?"这个房间是二楼一号房间一号门，可能与之后的积木块谜题有关"
            :doorNumber==2
                ?"这个房间是二楼一号房间二号门，可能与之后的收纳盒谜题有关"
                :"这个房间是二楼一号房间三号门";
        StoryProgress.Instance.Enqueue("房门号", text);
    }
}
