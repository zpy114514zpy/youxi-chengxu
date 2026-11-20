using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-30)]
public sealed class StoryProgress : MonoBehaviour
{
    public static StoryProgress Instance { get; private set; }
    public readonly List<string> clues = new List<string>();
    public readonly List<string> fragments = new List<string>();

    struct Page
    {
        public string title, body;
        public Texture2D texture;
        public bool modal;
        public Page(string title, string body, Texture2D texture, bool modal)
        { this.title=title; this.body=body; this.texture=texture; this.modal=modal; }
    }

    readonly Queue<Page> pages = new Queue<Page>();
    readonly Queue<Page> readingPages = new Queue<Page>();
    Page? current, reading;
    Texture2D opening, detail, guide;
    Font font;
    bool lampIntroShown;
    public bool IsReading => reading.HasValue || current.HasValue && current.Value.modal;
    public bool HasFirstFragment => fragments.Contains("信封碎片①");

    void Awake()
    {
        Instance=this;
        font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","SimHei","Arial"},26);
        opening=Resources.Load<Texture2D>("StoryUI/opening-clean");
        detail=Resources.Load<Texture2D>("StoryUI/detail-clean");
        guide=Resources.Load<Texture2D>("StoryUI/guide-clean");
        LoadList("child_story_clues",clues);
        LoadList("child_story_fragments",fragments);
    }

    void Start()
    {
        AddPages("一封写给未来的信", "小时候的她生活明亮而幸福。她相信长大后的自己会一直快乐，于是把美好的心愿写进一封信，留给未来的自己。", opening, true);
        AddPages("多年以后", "成长带来接连的变故。曾经的期待一点点破碎，她变得疲惫，也不再相信童年写下的那些愿望。", opening, true);
        AddPages("重读旧信", "多年后，她偶然翻出那封尘封的信。稚嫩的字迹仍在祝福未来，却与眼前消沉的自己格格不入。", opening, true);
        AddPages("时光夹缝", "旧信撬开了一道时光夹缝。情绪翻涌之际，她的意识回到了童年的房间。屋外的白雾，是尚未到来的未来。", opening, true);
        AddPages("寻找灯光", "房间里有点黑，我需要先把灯打开", opening, true);
    }

    static void LoadList(string key,List<string> list)
    { foreach(var part in PlayerPrefs.GetString(key,"").Split('\n')) if(!string.IsNullOrEmpty(part)&&!list.Contains(part)) list.Add(part); }
    static void SaveList(string key,List<string> list)
    { PlayerPrefs.SetString(key,string.Join("\n",list));PlayerPrefs.Save(); }
    public void RecordClue(string clue)
    { if(string.IsNullOrEmpty(clue)||clues.Contains(clue))return;clues.Add(clue);SaveList("child_story_clues",clues); }

    public void Read(string title,string body,bool record=true)
    {
        readingPages.Clear();
        foreach(var part in Split(body,64)) readingPages.Enqueue(new Page(title,part,detail,true));
        reading=readingPages.Count>0?readingPages.Dequeue():(Page?)null;
        if(record)RecordClue(title+"："+body);
    }

    public void AddFragment(string name,string text)
    {
        if(!fragments.Contains(name))
        {
            fragments.Add(name);SaveList("child_story_fragments",fragments);
            RecordClue(name+"："+text);
        }
        if(name=="信封碎片①")
        {
            readingPages.Clear();
            reading=new Page("关卡一完成","恭喜通过关卡一：卧室书桌谜题",opening,true);
            Enqueue("新的目标","请前往二楼一号房间1号门，完成第二个解密任务。");
        }
    }

    public void Enqueue(string message) => Enqueue("提示",message);
    public void Enqueue(string title,string message) => AddPages(title,message,guide,false);

    void AddPages(string title,string body,Texture2D texture,bool modal)
    {
        foreach(var part in Split(body,64))pages.Enqueue(new Page(title,part,texture,modal));
        if(!current.HasValue && pages.Count>0)current=pages.Dequeue();
    }

    static IEnumerable<string> Split(string body,int limit)
    {
        if(string.IsNullOrEmpty(body)){yield return "";yield break;}
        int start=0;
        while(start<body.Length)
        {
            int end=Mathf.Min(start+limit,body.Length);
            if(end<body.Length)
                for(int i=end;i>start+limit/2;i--)
                    if("。！？；，、\n".IndexOf(body[i-1])>=0){end=i;break;}
            yield return body.Substring(start,end-start).Trim();
            start=end;
        }
    }

    public void OnLampTurnedOn()
    {
        var player=FindObjectOfType<PlayerController>();
        if(lampIntroShown || !player || player.transform.position.y<2.8f)return;
        lampIntroShown=true;
        Enqueue("二楼一号房间","你现在正处于二楼一号房间，此房间中有三个小房间，分别为1号门、2号门、3号门，开门有提示哦。");
        Enqueue("第一关《卧室书桌谜题》","书桌上可能有些线索");
    }

    void Update()
    {
        if(!Input.GetKeyDown(KeyCode.Space))return;
        if(reading.HasValue)
        { reading=readingPages.Count>0?readingPages.Dequeue():(Page?)null;return; }
        if(current.HasValue)current=pages.Count>0?pages.Dequeue():(Page?)null;
    }

    void OnGUI()
    {
        Page? page=reading.HasValue?reading:current;
        if(!page.HasValue)return;
        var p=page.Value;
        float width=Mathf.Min(Screen.width*.88f,1100f);
        float height=Mathf.Min(Screen.height*(p.texture==opening?.5f:.42f),width*(p.texture==opening?.33f:.27f));
        var rect=new Rect((Screen.width-width)*.5f,p.modal?(Screen.height-height)*.5f:Screen.height-height-45f,width,height);
        if(p.texture)
        {
            Rect uv=p.texture==opening?new Rect(.072f,.185f,.856f,.633f):p.texture==detail?new Rect(.003f,.201f,.994f,.6f):new Rect(0f,0f,1f,1f);
            GUI.DrawTextureWithTexCoords(rect,p.texture,uv);
        }
        else GUI.Box(rect,"");
        float inset=width*.105f;
        float scale=Mathf.Clamp(width/1000f,.68f,1f);
        bool isGuide=p.texture==guide;
        GUI.Label(new Rect(rect.x+inset,rect.y+height*(isGuide ? .19f : .12f),width-2*inset,height*(isGuide ? .16f : .18f)),p.title,Style(Mathf.RoundToInt(26*scale),TextAnchor.MiddleCenter));
        GUI.Label(new Rect(rect.x+inset,rect.y+height*(isGuide ? .40f : .34f),width-2*inset,height*(isGuide ? .31f : .40f)),p.body,Style(Mathf.RoundToInt(23*scale),TextAnchor.MiddleCenter));
        GUI.Label(new Rect(rect.x+inset,rect.y+height*.77f,width-2*inset-12f,height*.12f),"空格继续",Style(Mathf.RoundToInt(19*scale),TextAnchor.MiddleRight));
    }

    GUIStyle Style(int size,TextAnchor align)
    { return new GUIStyle(GUI.skin.label){font=font,fontSize=size,alignment=align,wordWrap=true,normal={textColor=Color.white}}; }
    void OnDestroy(){if(Instance==this)Instance=null;if(font)Destroy(font);}
}
