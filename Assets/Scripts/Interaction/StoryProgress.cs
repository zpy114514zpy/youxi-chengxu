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
        public bool center;
        public Page(string title, string body, Texture2D texture, bool modal, bool center)
        { this.title=title; this.body=body; this.texture=texture; this.modal=modal; this.center=center; }
    }

    readonly Queue<Page> pages = new Queue<Page>();
    readonly Queue<Page> readingPages = new Queue<Page>();
    Page? current, reading;
    // 左下角“上一步”用的历史：走过的每一页都记下来，回看时只切换显示，不动上面两条队列，
    // 所以回看多少步都不会把后面的提示弄丢，按空格还能原路走回原处再继续。
    readonly List<Page> backSteps = new List<Page>();
    readonly Stack<Page> forwardSteps = new Stack<Page>();
    Page? reviewing;
    bool pendingBack;
    Texture2D opening, detail, guide, pillOn, pillHover, pillOff;
    Font font;
    bool lampIntroShown, cursorReleased;
    // 屏幕上当前这一页的标识（标题+正文+底图）：用来判断“有没有新的一页弹出来”，
    // 有新页出现就响一声提示音（和按空格翻页用同一段「消息提示音」）。
    string lastPageKey;
    // 第二关有自己的叙事 UI（Level2Ui）。这个组件仍然留着一个 Instance 给别人判空，
    // 但开场页、空格翻页、OnGUI 全部关掉，不然两套 UI 会一起弹。
    bool storyActive = true;
    PlayerInventory inventory;
    public bool IsReading => reading.HasValue || current.HasValue && current.Value.modal;
    // 当前是否还有任意一页剧情/提示正在屏幕上（用来判断玩家是否已经把 UI 跳过）。
    public bool IsShowingPage => current.HasValue || reading.HasValue || reviewing.HasValue;
    // 屏幕正在显示的那一页：回看时是历史页，否则是正常推进到的那一页。
    Page? DisplayedPage => reviewing.HasValue ? reviewing : (reading.HasValue ? reading : current);
    // 屏幕上或队列里还有页面没播完：新的提示要等这些播完再弹，避免两条 UI 挤在一起。
    public bool IsBusy => current.HasValue || reading.HasValue || pages.Count>0 || readingPages.Count>0;
    public bool HasFirstFragment => fragments.Contains("信封碎片①");

    void Awake()
    {
        Instance=this;
        storyActive = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Level 2";
        font=Font.CreateDynamicFontFromOSFont(new[]{"Microsoft YaHei","SimHei","Arial"},26);
        opening=Resources.Load<Texture2D>("StoryUI/opening-clean");
        detail=Resources.Load<Texture2D>("StoryUI/detail-clean");
        guide=Resources.Load<Texture2D>("StoryUI/guide-clean");
        LoadList("child_story_clues",clues);
        LoadList("child_story_fragments",fragments);
        EnsurePill();
    }

    void Start()
    {
        if(!storyActive)return;
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
        ClearReview();                        // 有新内容要看，先退出回看状态
        readingPages.Clear();
        foreach(var part in Split(body,64)) readingPages.Enqueue(new Page(title,part,detail,true,false));
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
            // 顺序：先读「碎片一」这一页，玩家按空格关掉之后，才把“去一号门”的目标排到后面。
            ClearReview();
            readingPages.Clear();
            reading=new Page("碎片一",
                "“以后我一定会好好爱护我的玩偶的！”\n\n—— 小时候的她，写在信封碎片上",
                detail,true,false);
            Enqueue("新的目标","请前往二楼一号房间1号门，完成第二个解密任务。");
        }
        else if(name=="信封碎片②")
        {
            OnBlockTaskCompleted();          // 第二个任务：二楼一号房间的积木块谜题
        }
        else if(name=="信封碎片③")
        {
            OnPasswordTaskCompleted();       // 第三个任务：密码箱
        }
        else if(name=="信封碎片④")
        {
            OnRoom2TidyCompleted();          // 第四个任务：二楼二号房间整理
        }
        else if(name=="信封碎片⑤")
        {
            OnClockTaskCompleted();          // 第五个任务：一楼客厅时钟
        }
    }

    // 已经提示过的内容（标题+正文）。同一条提示只弹一次，玩家反复按 E、来回走都不会刷屏。
    readonly HashSet<string> shownHints=new HashSet<string>();

    public void Enqueue(string message) => Enqueue("提示",message);
    /// <summary>右下角提示面板。同一句提示整局只出现一次。</summary>
    public void Enqueue(string title,string message)
    {
        if(!MarkHint(title,message))return;
        AddPages(title,message,guide,false);
    }
    /// <summary>屏幕正中的提示（不挡操作，空格继续）。同一句提示整局只出现一次。</summary>
    public void EnqueueCenter(string title,string message)
    {
        if(!MarkHint(title,message))return;
        AddPages(title,message,guide,false,true);
    }

    bool MarkHint(string title,string message)
    {
        var shown=DisplayedPage;
        if(shown.HasValue && shown.Value.title==title && shown.Value.body==message)return false;  // 同一页正在屏幕上
        return shownHints.Add((title??"")+"|"+(message??""));                                     // 说过的不再说
    }

    /// <summary>第二个任务（积木块谜题）完成后的收尾：先读碎片二，再给“去二号门”的新目标。</summary>
    public void OnBlockTaskCompleted()
    {
        pages.Clear();
        current=null;
        ClearReview();
        readingPages.Clear();
        reading=new Page("碎片二",
            "“长大之后我一定会做个追求自己热爱的、开心的人！”\n\n—— 小时候的她，写在信封碎片上",
            detail,true,false);
        // 顺序和碎片一一致：玩家按空格关掉「碎片二」这一页之后，才弹“去二号门”的目标。
        Enqueue("新的目标","请前往二楼一号房间2号门，完成第三个解密任务。");
        // 提示排进去的这一刻，二号门才可以打开（在这之前一直锁着，防止提前跳关）。
        RoomDoorGate.UnlockDoorTwo();
    }

    /// <summary>第三个任务（密码箱谜题）完成后的收尾：读碎片三，并把玩家引向二楼二号房间。</summary>
    public void OnPasswordTaskCompleted()
    {
        pages.Clear();
        current=null;
        ClearReview();
        readingPages.Clear();
        reading=new Page("碎片三",
            "“希望我以后的每个生日爸爸妈妈都会陪着我，一起幸福的度过！”\n\n—— 小时候的她，写在信封碎片上",
            detail,true,false);
        Enqueue("任务完成","恭喜完成第三个解密任务！");
        Enqueue("提示","现在还剩两张信封碎片才可集齐，进入下一关卡。");
        Enqueue("新的目标","隔壁房间好像需要你去搜索解密，请前往二楼二号房间！");
    }

    /// <summary>第四个任务（房间整理）完成后的收尾：读碎片四，然后给最后一个目标。</summary>
    public void OnRoom2TidyCompleted()
    {
        pages.Clear();
        current=null;
        ClearReview();
        readingPages.Clear();
        reading=new Page("碎片四",
            "“我希望家里永远是干干净净的！”\n\n—— 小时候的她，写在信封碎片上",
            detail,true,false);
        // 顺序：先关掉「碎片四」这一页，再依次提示“恭喜完成”和“前往一楼客厅”。
        Enqueue("任务完成","恭喜完成第四个解密任务");
        Enqueue("新的目标","请前往一楼客厅完成最后一个解密任务！");
    }

    /// <summary>第五个任务（客厅时钟）完成后的收尾：先读碎片五，再指引去二楼放置全部碎片。</summary>
    public void OnClockTaskCompleted()
    {
        pages.Clear();
        current=null;
        ClearReview();
        readingPages.Clear();
        reading=new Page("碎片五",
            "“愿望破灭的Moment！”\n\n—— 小时候的她，写在信封碎片上",
            detail,true,false);
        // 顺序：玩家按空格关掉「碎片五」这一页之后，才弹“去二楼放置碎片”的目标。
        EnqueueCenter("信封碎片","请将获取的所有信封碎片放置在二楼获取钥匙的地方！");
    }

    public void OnBedroomPuzzleSolved()
    {
        pages.Clear();
        current=null;
        AddPages("第一关《卧室书桌谜题》","恭喜通过关卡一：卧室书桌谜题",opening,true);
        AddPages("书桌抽屉","四个玩偶都回到了床上。书桌中间那个抽屉“咔哒”响了一声。",opening,true);
        // 去一号门的目标不在这里给：要先等玩家捡到碎片、读完「碎片一」那一页（见 AddFragment）。
    }

    void AddPages(string title,string body,Texture2D texture,bool modal,bool center=false)
    {
        ClearReview();                        // 新提示来了就直接显示，不要被回看页挡住
        foreach(var part in Split(body,64))pages.Enqueue(new Page(title,part,texture,modal,center));
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
        if(!storyActive)return;
        UpdateCursor();                       // 提示页在屏幕上时放开鼠标，方便点“上一步”
        WatchPageAppear();                    // 新的一页弹到屏幕上时也响一声
        if(pendingBack){pendingBack=false;GoBack();}
        if(UiInputBlocker.Active)return;      // 密码键盘打开时不推进剧情
        if(Input.GetKeyDown(KeyCode.Space))Advance();
    }

    /// <summary>
    /// 面板刚出现在屏幕上（例如按 E 读绘本、任务完成弹提示、排队的下一页自动接上）时响一声。
    /// 按空格翻页那一下已经响过，<see cref="GameAudio.PlayPage"/> 内部有 0.12 秒去重，不会连响两声。
    /// </summary>
    void WatchPageAppear()
    {
        var shown=DisplayedPage;
        string key=shown.HasValue
            ? (shown.Value.title??"")+"|"+(shown.Value.body??"")+"|"+(shown.Value.texture?shown.Value.texture.name:"")
            : null;
        if(key==lastPageKey)return;
        lastPageKey=key;
        if(key!=null)GameAudio.PlayPage();
    }

    /// <summary>往前推进一页：回看中按空格先沿原路走回原处，走回原处后恢复正常推进。</summary>
    void Advance()
    {
        // 一页都没有的时候按空格什么都不做，也不该出声。
        if(!reviewing.HasValue && !reading.HasValue && !current.HasValue)return;
        GameAudio.PlayPage();                 // 提示翻页音（按空格继续）
        if(reviewing.HasValue)
        {
            Page leaving=reviewing.Value;
            reviewing=null;
            PushBack(leaving);                // 回看往前走，这一页又变成“上一步”
            if(forwardSteps.Count>0)
            {
                Page next=forwardSteps.Pop();
                if(forwardSteps.Count>0)reviewing=next;   // 还没走回原处，继续显示历史页
            }
            return;
        }
        if(reading.HasValue)PushBack(reading.Value);
        else if(current.HasValue)PushBack(current.Value);
        if(reading.HasValue)
        { reading=readingPages.Count>0?readingPages.Dequeue():(Page?)null;return; }
        if(current.HasValue)current=pages.Count>0?pages.Dequeue():(Page?)null;
    }

    /// <summary>左下角“上一步”：回看上一页提示，可以连点继续往前翻。</summary>
    void GoBack()
    {
        if(backSteps.Count==0)return;
        GameAudio.PlayPage();                 // 提示翻页音（点“上一步”）
        if(DisplayedPage.HasValue)forwardSteps.Push(DisplayedPage.Value);
        reviewing=backSteps[backSteps.Count-1];
        backSteps.RemoveAt(backSteps.Count-1);
    }

    void PushBack(Page page)
    {
        backSteps.Add(page);
        while(backSteps.Count>40)backSteps.RemoveAt(0);   // 只留最近 40 页，别越攒越多
    }

    /// <summary>有新提示要显示时退出回看，但已经走过的历史仍然保留，“上一步”照样能用。</summary>
    void ClearReview()
    {
        reviewing=null;
        forwardSteps.Clear();
    }

    /// <summary>
    /// 提示页显示时把鼠标放开，玩家才能点左下角的“上一步”；
    /// 提示页收掉后重新锁回去（如果背包、密码键盘这些界面还开着，就不抢）。
    /// </summary>
    void UpdateCursor()
    {
        if(IsShowingPage)
        {
            if(Cursor.lockState==CursorLockMode.Locked)
            { Cursor.lockState=CursorLockMode.None;Cursor.visible=true;cursorReleased=true; }
            return;
        }
        if(!cursorReleased)return;
        cursorReleased=false;
        if(!inventory)inventory=FindObjectOfType<PlayerInventory>();
        if(UiInputBlocker.Active||inventory&&inventory.IsOpen)return;
        Cursor.lockState=CursorLockMode.Locked;
        Cursor.visible=false;
    }

    void OnGUI()
    {
        if(!storyActive)return;
        var shown=DisplayedPage;
        if(!shown.HasValue)return;
        var p=shown.Value;
        float width=Mathf.Min(Screen.width*.88f,1100f);
        float height=Mathf.Min(Screen.height*(p.texture==opening?.5f:.42f),width*(p.texture==opening?.33f:.27f));
        bool isGuide=p.texture==guide;
        var rect=new Rect((Screen.width-width)*.5f,PanelTop(p,height,isGuide),width,height);
        if(p.texture)
        {
            Rect uv=p.texture==opening?new Rect(.072f,.185f,.856f,.633f):p.texture==detail?new Rect(.003f,.201f,.994f,.6f):new Rect(0f,0f,1f,1f);
            GUI.DrawTextureWithTexCoords(rect,p.texture,uv);
        }
        else GUI.Box(rect,"");
        float inset=width*.105f;
        float scale=Mathf.Clamp(width/1000f,.68f,1f);
        // guide 贴图上方 0~19% 是锁形装饰、不在面板内，标题必须压在装饰下方并水平居中。
        float titleY = isGuide ? .255f : .12f;
        float titleH = isGuide ? .15f : .18f;
        float bodyY  = isGuide ? .45f : .34f;
        float bodyH  = isGuide ? .30f : .40f;
        GUI.Label(new Rect(rect.x+inset,rect.y+height*titleY,width-2*inset,height*titleH),p.title,Style(Mathf.RoundToInt(26*scale),TextAnchor.MiddleCenter));
        GUI.Label(new Rect(rect.x+inset,rect.y+height*bodyY,width-2*inset,height*bodyH),p.body,Style(Mathf.RoundToInt(23*scale),TextAnchor.MiddleCenter));
        // 底部一行：左下角“上一步”（可点击回看），右下角“空格继续”，左右对称。
        int hintSize=Mathf.RoundToInt(19*scale);
        float rowY=rect.y+height*.77f, rowH=height*.12f, rowW=width-2*inset-12f;
        GUI.Label(new Rect(rect.x+inset,rowY,rowW,rowH),"空格继续",Style(hintSize,TextAnchor.MiddleRight));
        float pillH=Mathf.Clamp(rowH*.9f,32f,46f);
        float pillW=Mathf.Min(rowW*.42f,170f*scale);
        var backRect=new Rect(rect.x+inset,rowY+(rowH-pillH)*.5f,pillW,pillH);
        if(BackButton(backRect,hintSize,backSteps.Count>0))pendingBack=true;
    }

    /// <summary>“上一步”按钮：没有历史时是灰的、点不动；有历史时可点，悬停/按下会变色。</summary>
    bool BackButton(Rect rect,int size,bool enabled)
    {
        EnsurePill();
        var style=new GUIStyle(GUI.skin.label){
            font=font,fontSize=size,alignment=TextAnchor.MiddleCenter,wordWrap=false,
            border=new RectOffset(PillBorder,PillBorder,PillBorder,PillBorder),
            padding=new RectOffset(0,0,0,0),margin=new RectOffset(0,0,0,0)};
        if(!enabled)
        {
            style.normal.background=pillOff;
            style.normal.textColor=new Color(1f,1f,1f,.34f);
            GUI.Label(rect,"←  上一步",style);
            return false;
        }
        style.normal.background=pillOn;
        style.hover.background=pillHover;
        style.active.background=pillHover;
        style.onNormal.background=pillOn;
        style.onHover.background=pillHover;
        style.onActive.background=pillHover;
        style.normal.textColor=new Color(1f,1f,1f,.94f);
        style.hover.textColor=new Color(1f,.9f,.55f,1f);
        style.active.textColor=new Color(1f,.82f,.35f,1f);
        style.onNormal.textColor=style.normal.textColor;
        style.onHover.textColor=style.hover.textColor;
        style.onActive.textColor=style.active.textColor;
        return GUI.Button(rect,"←  上一步",style);
    }

    // 圆角药丸底图：横向拉伸靠 9 宫格边框，所以只用一张 64×64 的小图，不会把圆角拉糊。
    const int PillBorder=14;
    void EnsurePill()
    {
        if(pillOn)return;
        pillOn=PillTexture(.13f);
        pillHover=PillTexture(.26f);
        pillOff=PillTexture(.05f);
    }

    static Texture2D PillTexture(float alpha)
    {
        const int n=64,r=14;
        var tex=new Texture2D(n,n,TextureFormat.RGBA32,false)
        { hideFlags=HideFlags.HideAndDontSave,filterMode=FilterMode.Bilinear,wrapMode=TextureWrapMode.Clamp };
        var pixels=new Color32[n*n];
        for(int y=0;y<n;y++)
            for(int x=0;x<n;x++)
            {
                float qx=Mathf.Abs(x+.5f-n*.5f)-(n*.5f-r), qy=Mathf.Abs(y+.5f-n*.5f)-(n*.5f-r);
                float d=Mathf.Sqrt(Mathf.Max(qx,0f)*Mathf.Max(qx,0f)+Mathf.Max(qy,0f)*Mathf.Max(qy,0f))
                    +Mathf.Min(Mathf.Max(qx,qy),0f)-r;
                float cover=Mathf.Clamp01(.5f-d);        // 边缘一个像素做抗锯齿
                pixels[y*n+x]=new Color32(255,255,255,(byte)Mathf.RoundToInt(cover*alpha*255f));
            }
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// 提示面板的纵向位置。所有“提示 / 新的目标 / 房间介绍 / 线索”这类小面板统一停在一个高度：
    /// 屏幕高度的 66%（比正中低一截，又不贴底），换文案、换房间都不会跳位置。
    /// 剧情大页（opening/detail）仍然居中，那两页是整屏阅读。
    /// </summary>
    static float PanelTop(Page p,float height,bool isGuide)
    {
        float y;
        if(isGuide) y=Screen.height*.66f-height*.5f;
        else y=p.modal?(Screen.height-height)*.5f:Screen.height-height-45f;
        return Mathf.Clamp(y,24f,Mathf.Max(24f,Screen.height-height-24f));
    }

    GUIStyle Style(int size,TextAnchor align)
    { return new GUIStyle(GUI.skin.label){font=font,fontSize=size,alignment=align,wordWrap=true,normal={textColor=Color.white}}; }
    void OnDestroy()
    {
        if(Instance==this)Instance=null;
        if(font)Destroy(font);
        if(pillOn)Destroy(pillOn);
        if(pillHover)Destroy(pillHover);
        if(pillOff)Destroy(pillOff);
    }
}
