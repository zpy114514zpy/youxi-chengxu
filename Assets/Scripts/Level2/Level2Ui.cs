using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 第二关自己的 UI 层（完全不碰第一关的 StoryProgress）：
///   · Say    —— 屏幕正中的叙事页，按空格继续
///   · Note   —— 屏幕下方的目标提示
///   · Ask    —— 选项问答（鼠标点选）
///   · Letter —— 屏幕上摊开的一封信
///   · Ending —— 黑幕白字，一句一句浮出来
/// 全部走同一个队列，不会两条提示叠在一起。
/// </summary>
public sealed class Level2Ui : MonoBehaviour
{
    public static Level2Ui Instance { get; private set; }

    enum Kind { Center, Note, Ask, Letter, Ending }

    sealed class Page
    {
        public Kind kind;
        public string title, body;
        public string[] options, lines;
        public System.Action<int> answer;
        public System.Action done;
    }

    // 黑幕白字：一个字一个字往外打
    const float CharDelay = .075f;    // 每个字之间的间隔
    const float LinePause = .55f;     // 一句话打完，停一下再打下一句

    readonly Queue<Page> queue = new Queue<Page>();
    Page current;
    Font font;
    Texture2D white, letterPaper, adultFigure;
    float shownAt, charTimer;
    int typedLine, typedCount;
    bool blockerSet, mouseFreed;

    public bool IsShowing => current != null || queue.Count > 0;
    public bool IsModal => current != null &&
        (current.kind == Kind.Ask || current.kind == Kind.Letter || current.kind == Kind.Ending);

    void Awake()
    {
        Instance = this;
        font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 26);
        white = L2.SolidTexture(Color.white);
        letterPaper = Resources.Load<Texture2D>("Level2/letter-paper");
        // 镜子里“长大后”的那张图：放了真图就用真图，没放就用程序生成的剪影兜底。
        adultFigure = Resources.Load<Texture2D>("Level2/mirror-adult");
        if (!adultFigure) adultFigure = L2.AdultSilhouette();
    }

    // ---------------- 对外接口 ----------------

    /// <summary>屏幕正中的叙事页。</summary>
    public void Say(string title, string body) => Enqueue(new Page { kind = Kind.Center, title = title, body = body });

    /// <summary>屏幕下方的目标提示。</summary>
    public void Note(string title, string body) => Enqueue(new Page { kind = Kind.Note, title = title, body = body });

    /// <summary>选项问答。玩家点第 i 个选项后回调。</summary>
    public void Ask(string title, string question, string[] options, System.Action<int> answer)
        => Enqueue(new Page { kind = Kind.Ask, title = title, body = question, options = options, answer = answer });

    /// <summary>展开一封信（贴图 + 居中文字），按空格合上。</summary>
    public void Letter(string title, string body, System.Action onClose = null)
        => Enqueue(new Page { kind = Kind.Letter, title = title, body = body, done = onClose });

    /// <summary>黑幕白字。每一句自己浮出来，全部出完后按空格进入下一步。</summary>
    public void Ending(string title, string[] lines, System.Action onDone)
        => Enqueue(new Page { kind = Kind.Ending, title = title, lines = lines, done = onDone });

    void Enqueue(Page page)
    {
        if (current == null) { Enter(page); }
        else queue.Enqueue(page);
    }

    void Enter(Page page)
    {
        current = page;
        shownAt = Time.unscaledTime;
        charTimer = .35f;          // 黑幕先黑一下，再开始出字（这里存的是“还要等多久”，不是绝对时间）
        typedLine = 0;
        typedCount = 0;
        RefreshBlocker();
    }

    /// <summary>最后一句是不是已经一个字一个字打完了。</summary>
    bool AllTyped => current == null || current.kind != Kind.Ending || current.lines == null
                     || typedLine >= current.lines.Length;

    void Advance()
    {
        var done = current != null ? current.done : null;
        current = null;
        if (done != null) done();
        if (current == null && queue.Count > 0) Enter(queue.Dequeue());
        else RefreshBlocker();
    }

    /// <summary>把当前这一页跳过去（自检 / 调试用）。Ask 页需要 answer 回调，不走这里。</summary>
    public void Skip()
    {
        if (current == null) return;
        if (current.kind == Kind.Ask) return;
        if (current.kind == Kind.Ending && !AllTyped) { typedLine = current.lines.Length; typedCount = 0; }
        else Advance();
    }

    public bool IsAsk => current != null && current.kind == Kind.Ask;

    /// <summary>代替鼠标点击：直接选第 index 个选项（自检 / 调试用）。</summary>
    public void AnswerCurrent(int index)
    {
        if (current == null || current.kind != Kind.Ask) return;
        var answer = current.answer;
        Advance();
        answer?.Invoke(index);
    }

    void Update()
    {
        if (current == null) return;

        if (current.kind == Kind.Ending && current.lines != null && typedLine < current.lines.Length)
        {
            charTimer -= Time.unscaledDeltaTime;
            if (charTimer <= 0f)
            {
                int len = current.lines[typedLine]?.Length ?? 0;
                if (typedCount < len) { typedCount++; charTimer = CharDelay; }             // 再漏一个字
                else if (typedLine < current.lines.Length - 1)                             // 这句完了，停一下
                { typedLine++; typedCount = 0; charTimer = LinePause; }
                else { typedLine = current.lines.Length; typedCount = 0; }                 // 全部打完
            }
        }

        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (current.kind == Kind.Ask) return;                                  // 必须点选项
            if (!AllTyped) return;                                                 // 字没打完不给跳
            Advance();
        }
    }

    void RefreshBlocker()
    {
        bool modal = IsModal;
        if (modal && !blockerSet) { UiInputBlocker.Active = true; blockerSet = true; }
        else if (!modal && blockerSet) { UiInputBlocker.Active = false; blockerSet = false; }

        if (modal)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            mouseFreed = true;
        }
        else if (mouseFreed)
        {
            mouseFreed = false;
            var inv = FindObjectOfType<PlayerInventory>();
            if (!inv || !inv.IsOpen) { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; }
        }
    }

    void OnDisable()
    {
        if (blockerSet) { UiInputBlocker.Active = false; blockerSet = false; }
    }

    // ---------------- 绘制 ----------------

    GUIStyle Style(int size, TextAnchor anchor, Color color)
        => new GUIStyle(GUI.skin.label)
        { font = font, fontSize = size, alignment = anchor, wordWrap = true, normal = { textColor = color } };

    void Fill(Rect r, Color c)
    {
        var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = old;
    }

    void Panel(Rect r)
    {
        Fill(r, new Color(.02f, .04f, .07f, .90f));
        var edge = new Color(.36f, .70f, 1f, .55f);
        Fill(new Rect(r.x, r.y, r.width, 2f), edge);
        Fill(new Rect(r.x, r.yMax - 2f, r.width, 2f), edge);
        Fill(new Rect(r.x, r.y, 2f, r.height), edge);
        Fill(new Rect(r.xMax - 2f, r.y, 2f, r.height), edge);
    }

    void OnGUI()
    {
        if (current == null) return;
        switch (current.kind)
        {
            case Kind.Center: DrawCenter(); break;
            case Kind.Note: DrawNote(); break;
            case Kind.Ask: DrawAsk(); break;
            case Kind.Letter: DrawLetter(); break;
            case Kind.Ending: DrawEnding(); break;
        }
    }

    void DrawCenter()
    {
        float w = Mathf.Min(Screen.width * .78f, 940f);
        float h = 250f;
        var r = new Rect((Screen.width - w) * .5f, (Screen.height - h) * .5f, w, h);
        Panel(r);
        float pad = 34f;
        GUI.Label(new Rect(r.x + pad, r.y + 26f, r.width - pad * 2f, 46f), current.title ?? "", Style(29, TextAnchor.MiddleCenter, new Color(.62f, .86f, 1f)));
        GUI.Label(new Rect(r.x + pad, r.y + 82f, r.width - pad * 2f, h - 140f), current.body ?? "", Style(24, TextAnchor.UpperCenter, new Color(.95f, .96f, .98f)));
        GUI.Label(new Rect(r.x + pad, r.yMax - 50f, r.width - pad * 2f, 30f), "空格继续", Style(18, TextAnchor.MiddleRight, new Color(.72f, .80f, .88f, .9f)));
    }

    void DrawNote()
    {
        float w = Mathf.Min(Screen.width * .70f, 820f);
        float h = 120f;
        var r = new Rect((Screen.width - w) * .5f, Screen.height - h - 78f, w, h);
        Panel(r);
        GUI.Label(new Rect(r.x + 26f, r.y + 16f, r.width - 52f, 34f), current.title ?? "", Style(23, TextAnchor.MiddleLeft, new Color(.62f, .86f, 1f)));
        GUI.Label(new Rect(r.x + 26f, r.y + 50f, r.width - 52f, 52f), current.body ?? "", Style(21, TextAnchor.UpperLeft, new Color(.94f, .95f, .97f)));
    }

    void DrawAsk()
    {
        float w = Mathf.Min(Screen.width * .80f, 980f);
        float bodyH = 92f;
        float btnH = 62f, gap = 16f;
        float h = 96f + bodyH + current.options.Length * (btnH + gap) + 34f;
        var r = new Rect((Screen.width - w) * .5f, (Screen.height - h) * .5f, w, h);
        Panel(r);
        float pad = 40f;
        GUI.Label(new Rect(r.x + pad, r.y + 26f, r.width - pad * 2f, 44f), current.title ?? "", Style(27, TextAnchor.MiddleCenter, new Color(.66f, .88f, 1f)));
        GUI.Label(new Rect(r.x + pad, r.y + 76f, r.width - pad * 2f, bodyH), current.body ?? "", Style(23, TextAnchor.MiddleCenter, new Color(.95f, .96f, .98f)));

        var btnStyle = new GUIStyle(GUI.skin.label)
        {
            font = font,
            fontSize = 22,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            border = new RectOffset(0, 0, 0, 0)
        };
        btnStyle.normal.textColor = Color.white;
        btnStyle.hover.textColor = new Color(1f, .92f, .62f);
        btnStyle.active.textColor = new Color(1f, .82f, .35f);

        float y = r.y + 96f + bodyH;
        for (int i = 0; i < current.options.Length; i++)
        {
            var br = new Rect(r.x + pad, y, r.width - pad * 2f, btnH);
            bool hover = br.Contains(Event.current.mousePosition);
            Fill(br, hover ? new Color(.12f, .30f, .46f, .95f) : new Color(.06f, .13f, .20f, .92f));
            var edge = hover ? new Color(.55f, .85f, 1f) : new Color(.30f, .52f, .66f);
            Fill(new Rect(br.x, br.y, br.width, 2f), edge);
            Fill(new Rect(br.x, br.yMax - 2f, br.width, 2f), edge);
            Fill(new Rect(br.x, br.y, 2f, br.height), edge);
            Fill(new Rect(br.xMax - 2f, br.y, 2f, br.height), edge);
            if (GUI.Button(br, "① ② ③ ④ ⑤ ⑥ "[i * 2] + "  " + current.options[i], btnStyle))
            {
                var answer = current.answer;
                Advance();
                answer?.Invoke(i);
            }
            y += btnH + gap;
        }
    }

    void DrawLetter()
    {
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, .82f));

        float h = Mathf.Min(Screen.height * .82f, 760f);
        float w = h * .74f;
        var r = new Rect((Screen.width - w) * .5f, (Screen.height - h) * .5f, w, h);

        if (letterPaper) GUI.DrawTexture(r, letterPaper, ScaleMode.ScaleAndCrop, true);
        else Fill(r, new Color(.94f, .90f, .80f, 1f));

        float pad = w * .13f;
        GUI.Label(new Rect(r.x + pad, r.y + h * .16f, w - pad * 2f, 60f),
            current.title ?? "", Style(Mathf.RoundToInt(h * .052f), TextAnchor.MiddleCenter, new Color(.30f, .22f, .16f)));
        GUI.Label(new Rect(r.x + pad, r.y + h * .30f, w - pad * 2f, h * .42f),
            current.body ?? "", Style(Mathf.RoundToInt(h * .045f), TextAnchor.UpperCenter, new Color(.24f, .17f, .12f)));
        GUI.Label(new Rect(r.x + pad, r.yMax - h * .10f, w - pad * 2f, 32f),
            "空格合上", Style(17, TextAnchor.MiddleRight, new Color(.34f, .26f, .18f, .85f)));
    }

    void DrawEnding()
    {
        Fill(new Rect(0, 0, Screen.width, Screen.height), Color.black);

        float w = Mathf.Min(Screen.width * .70f, 880f);
        float x = (Screen.width - w) * .5f;
        float y = Screen.height * .28f;

        // 标题也淡入，不要一下就砸在黑幕上
        float head = Mathf.Clamp01((Time.unscaledTime - shownAt) / .55f);
        GUI.Label(new Rect(x, y, w, 50f), current.title ?? "", Style(26, TextAnchor.MiddleCenter, new Color(.78f, .80f, .84f, head)));
        y += 74f;

        int total = current.lines == null ? 0 : current.lines.Length;
        for (int i = 0; i < typedLine && i < total; i++)
        {
            GUI.Label(new Rect(x, y, w, 58f), current.lines[i], Style(25, TextAnchor.MiddleCenter, new Color(.96f, .96f, .96f)));
            y += 62f;
        }

        // 正在打的那一句：只画已经漏出来的那几个字
        if (typedLine < total && typedCount > 0)
        {
            string line = current.lines[typedLine] ?? "";
            string part = line.Substring(0, Mathf.Min(typedCount, line.Length));
            float fade = Mathf.Clamp01(typedCount / 6f);
            GUI.Label(new Rect(x, y, w, 58f), part, Style(25, TextAnchor.MiddleCenter, new Color(.96f, .96f, .96f, fade)));
        }

        if (AllTyped && total > 0)
            GUI.Label(new Rect(x, Screen.height - 58f, w, 34f), "空格继续", Style(18, TextAnchor.MiddleCenter, new Color(.62f, .64f, .68f)));
    }

    /// <summary>给镜子用的成人剪影贴图（黑幕之外也可以拿来当镜面层）。</summary>
    public Texture2D AdultFigure => adultFigure;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (font) Destroy(font);
        if (white) Destroy(white);
        if (adultFigure) Destroy(adultFigure);
        if (blockerSet) UiInputBlocker.Active = false;
    }
}
