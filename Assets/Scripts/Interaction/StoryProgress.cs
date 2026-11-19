using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-30)]
public sealed class StoryProgress : MonoBehaviour
{
    public static StoryProgress Instance { get; private set; }
    public readonly List<string> clues = new List<string>();
    public readonly List<string> fragments = new List<string>();
    readonly Queue<string> messages = new Queue<string>();
    Texture2D detail, guide, toast;
    Font font;
    string currentMessage, detailTitle, detailBody;
    float messageUntil;
    public bool IsReading => !string.IsNullOrEmpty(detailBody);
    public bool HasFirstFragment => fragments.Contains("信封碎片①");

    void Awake()
    {
        Instance=this;
        font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","SimHei","Arial"},24);
        detail=Resources.Load<Texture2D>("StoryUI/detail");
        guide=Resources.Load<Texture2D>("StoryUI/guide");
        toast=Resources.Load<Texture2D>("StoryUI/toast");
        LoadList("child_story_clues",clues);LoadList("child_story_fragments",fragments);
    }
    static void LoadList(string key,List<string> list)
    {foreach(var part in PlayerPrefs.GetString(key,"").Split('\n'))if(!string.IsNullOrEmpty(part)&&!list.Contains(part))list.Add(part);}
    static void SaveList(string key,List<string> list){PlayerPrefs.SetString(key,string.Join("\n",list));PlayerPrefs.Save();}
    public void RecordClue(string clue)
    {if(string.IsNullOrEmpty(clue)||clues.Contains(clue))return;clues.Add(clue);SaveList("child_story_clues",clues);}
    public void Read(string title,string body,bool record=true)
    {detailTitle=title;detailBody=body;if(record)RecordClue(title+"："+body);}
    public void AddFragment(string name,string text)
    {
        if(fragments.Contains(name))return;
        fragments.Add(name);SaveList("child_story_fragments",fragments);
        RecordClue(name+"："+text);
        if(name=="信封碎片①")
        {Enqueue("恭喜你成功解锁第一个解密任务！");Enqueue("请玩家前往二楼一号房间1号门完成第二个解密任务。");}
    }
    public void Enqueue(string message)
    {messages.Enqueue(message);if(string.IsNullOrEmpty(currentMessage))Next();}
    void Next(){currentMessage=messages.Count>0?messages.Dequeue():null;messageUntil=Time.unscaledTime+12f;}
    public void OnLampTurnedOn()
    {
        var p=GetComponent<PlayerController>();
        if(!p || transform.position.y<2.8f)return;
        if(PlayerPrefs.GetInt("child_story_room_intro",0)!=0)return;
        PlayerPrefs.SetInt("child_story_room_intro",1);PlayerPrefs.Save();
        Enqueue("你现在正处于二楼一号房间。此房间中有三个小房间，分别为1号门、2号门、3号门。");
    }
    void Update()
    {
        if(IsReading)
        {
            if(Input.GetKeyDown(KeyCode.Space)){detailBody=null;messageUntil=Time.unscaledTime+12f;}
            return;
        }
        if(!string.IsNullOrEmpty(currentMessage)&&(Input.GetKeyDown(KeyCode.Space)||Time.unscaledTime>messageUntil))Next();
    }
    void OnGUI()
    {
        if(IsReading)
        {
            float w=Mathf.Min(Screen.width*.75f,820f),h=Mathf.Min(Screen.height*.32f,210f);
            var r=new Rect((Screen.width-w)*.5f,(Screen.height-h)*.5f,w,h);
            if(detail)GUI.DrawTextureWithTexCoords(r,detail,new Rect(.125f,.26f,.75f,.43f));else GUI.Box(r,"");
            GUI.Label(new Rect(r.x+35,r.y+22,w-70,35),detailTitle,Style(24,TextAnchor.MiddleCenter));
            GUI.Label(new Rect(r.x+45,r.y+61,w-90,h-76),detailBody+"\n空格继续",Style(21,TextAnchor.MiddleCenter));
            return;
        }
        if(string.IsNullOrEmpty(currentMessage))return;
        bool intro=currentMessage.StartsWith("你现在");
        float width=Mathf.Min(Screen.width*.68f,intro?640f:780f),height=intro?140f:170f;
        var rect=new Rect((Screen.width-width)*.5f,Mathf.Min(Screen.height*.68f,Screen.height-height-16f),width,height);
        var texture=intro?toast:guide;
        if(texture)GUI.DrawTextureWithTexCoords(rect,texture,intro?new Rect(.10f,.215f,.78f,.565f):new Rect(.125f,.32f,.755f,.51f));else GUI.Box(rect,"");
        GUI.Label(new Rect(rect.x+37,rect.y+17,width-74,height-34),currentMessage+"\n空格跳过",Style(intro?19:22,TextAnchor.MiddleCenter));
    }
    GUIStyle Style(int size,TextAnchor align)=>new GUIStyle(GUI.skin.label){font=font,fontSize=size,alignment=align,wordWrap=true,normal={textColor=Color.white}};
    void OnDestroy(){if(Instance==this)Instance=null;if(font)Destroy(font);}
}
