using UnityEngine;

public sealed class DoorRoomIntro : MonoBehaviour
{
    public int doorNumber = 1;
    [TextArea(2, 5)] public string introduction;

    /// <summary>这扇门是否已经播过“房间介绍”。密码箱用它在介绍之后才提示找凳子。</summary>
    public bool HasIntroduced { get; private set; }

    public void OnOpened()
    {
        HasIntroduced = true;
        if (!StoryProgress.Instance) return;
        string text=doorNumber==1
            ?"这个房间是二楼一号房间一号门，可能与之后的积木块谜题有关"
            :doorNumber==2
                ?"这个房间是二楼一号房间二号门，可能与之后的收纳盒谜题有关"
                :"这个房间是二楼一号房间三号门";
        // 只播房间号这一条。原来紧跟着的「谜题三《积木块谜题》」目标提示按需求去掉。
        StoryProgress.Instance.Enqueue("房门号", text);
    }
}
