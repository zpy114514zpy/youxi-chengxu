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
    // 左下角“上一步”用的历史：每页看完都记下来，回看时只换“显示哪一页”，
    // 不动 queue，所以回看多少步都不会把后面的提示弄丢，按空格还能原路走回原处再继续。
    readonly List<Page> backSteps = new List<Page>();
    readonly Stack<Page> forwardSteps = new Stack<Page>();
    Page reviewing;
    bool pendingBack;
    Texture2D pillOn, pillHover, pillOff;
    Font font;
    Texture2D white, letterPaper, adultFigure;
    float shownAt, charTimer;
    int typedLine, typedCount;
    bool blockerSet, mouseFreed;

    /// <summary>屏幕上看得到的那一页：回看时是历史页，否则是正常推进到的那一页。</summary>
    Page Displayed => reviewing != null ? reviewing : current;
    /// <summary>屏幕上确实有东西（队列里排着但还没轮到的不算）。</summary>
    public bool IsPageOnScreen => Displayed != null;

    public bool IsShowing => current != null || reviewing != null || queue.Count > 0;
    public bool IsModal => Displayed != null &&
        (Displayed.kind == Kind.Ask || Displayed.kind == Kind.Letter || Displayed.kind == Kind.Ending);

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

    /// <summary>调试/自检用：当前这一页的标题与正文。</summary>
    public string CurrentTitle => current?.title ?? "";
    public string CurrentBody => current?.body ?? "";

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
        // 有新页要弹出来，就先退出回看（历史仍保留，“上一步”照样能用）。
        reviewing = null;
        forwardSteps.Clear();
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
        if (current == null) return;
        PushBack(current);          // 关掉的这一页变成“上一步”，随时能回来看
        var done = current.done;
        current = null;
        if (done != null) done();
        if (current == null && queue.Count > 0) Enter(queue.Dequeue());
        else RefreshBlocker();
    }

    /// <summary>左下角“上一步”：回看上一页提示，可以连点继续往前翻。</summary>
    void GoBack()
    {
        if (backSteps.Count == 0) return;
        GameAudio.PlayPage();                                  // 提示翻页音（点“上一步”）
        var shown = Displayed;
        if (shown != null) forwardSteps.Push(shown);
        reviewing = backSteps[backSteps.Count - 1];
        backSteps.RemoveAt(backSteps.Count - 1);
        RefreshBlocker();
    }

    /// <summary>回看中按空格：沿原路走回原处，走到头就回到当前这一页继续。</summary>
    void StepForward()
    {
        if (reviewing == null) return;
        Page leaving = reviewing;
        reviewing = null;
        PushBack(leaving);
        if (forwardSteps.Count > 0)
        {
            Page next = forwardSteps.Pop();
            if (forwardSteps.Count > 0) reviewing = next;      // 还没走回原处，继续显示历史页
        }
        RefreshBlocker();
    }

    void PushBack(Page page)
    {
        backSteps.Add(page);
        while (backSteps.Count > 40) backSteps.RemoveAt(0);    // 只留最近 40 页，别越攒越多
    }

    /// <summary>把当前这一页跳过去（自检 / 调试用）。Ask 页需要 answer 回调，不走这里。</summary>
    public void Skip()
    {
        if (reviewing != null) { StepForward(); return; }
        if (current == null) return;
        if (current.kind == Kind.Ask) return;
        if (current.kind == Kind.Ending && !AllTyped) { typedLine = current.lines.Length; typedCount = 0; }
        else Advance();
    }

    public bool IsAsk => current != null && current.kind == Kind.Ask;

    /// <summary>代替鼠标点击：直接选第 index 个选项（自检 / 调试用）。</summary>
    public void AnswerCurrent(int index)
    {
        if (reviewing != null) return;                 // 回看中的问答页是只读的，不能重复作答
        if (current == null || current.kind != Kind.Ask) return;
        var answer = current.answer;
        Advance();
        answer?.Invoke(index);
    }

    void Update()
    {
        // GUI 里不能直接改状态，按钮只置一个标记，这里统一处理。
        if (pendingBack) { pendingBack = false; GoBack(); }

        if (current == null && reviewing == null) return;

        // 回看状态：空格是“返回”，点左下角还能继续往前翻。
        if (reviewing != null)
        {
            if (Input.GetKeyDown(KeyCode.Space)) { GameAudio.PlayPage(); StepForward(); }
            return;
        }

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
            GameAudio.PlayPage();                                                  // 提示翻页音
            Advance();
        }
    }

    void RefreshBlocker()
    {
        bool modal = IsModal;
        if (modal && !blockerSet) { UiInputBlocker.Active = true; blockerSet = true; }
        else if (!modal && blockerSet) { UiInputBlocker.Active = false; blockerSet = false; }

        // 只要屏幕上有提示框就把鼠标放开，玩家才能点左下角的“上一步”；
        // 提示框收掉后再锁回去（背包还开着就不抢）。
        bool showing = Displayed != null;
        if (showing && !mouseFreed)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            mouseFreed = true;
        }
        else if (!showing && mouseFreed)
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
    {
        var st = new GUIStyle(GUI.skin.label)
        { font = font, fontSize = size, alignment = anchor, wordWrap = true };
        st.normal.textColor = color;
        st.hover.textColor = color;
        st.active.textColor = color;
        st.focused.textColor = color;
        st.onNormal.textColor = color;
        return st;
    }

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
        var p = Displayed;
        if (p == null) return;
        // 回看中的问答页是只读的：不能把选项画成按钮，否则会把答案又提交一遍。
        if (reviewing != null && p.kind == Kind.Ask) { DrawPlain(p); return; }
        switch (p.kind)
        {
            case Kind.Center: DrawCenter(p); break;
            case Kind.Note: DrawNote(p); break;
            case Kind.Ask: DrawAsk(p); break;
            case Kind.Letter: DrawLetter(p); break;
            case Kind.Ending: DrawEnding(p, reviewing != null); break;
        }
    }

    void DrawCenter(Page p)
    {
        float w = Mathf.Min(Screen.width * .78f, 940f);
        float h = 250f;
        var r = new Rect((Screen.width - w) * .5f, (Screen.height - h) * .5f, w, h);
        Panel(r);
        float pad = 34f;
        GUI.Label(new Rect(r.x + pad, r.y + 26f, r.width - pad * 2f, 46f), p.title ?? "", Style(29, TextAnchor.MiddleCenter, new Color(.62f, .86f, 1f)));
        GUI.Label(new Rect(r.x + pad, r.y + 82f, r.width - pad * 2f, h - 140f), p.body ?? "", Style(24, TextAnchor.UpperCenter, new Color(.95f, .96f, .98f)));
        DrawBottomRow(r, r.yMax - 52f, pad, 34f, false);
    }

    void DrawNote(Page p)
    {
        float w = Mathf.Min(Screen.width * .70f, 820f);
        const int titleSize = 23, bodySize = 21;
        float lineH = Mathf.Round(bodySize * 1.46f);

        // 面板高度按正文实际要占几行来定 —— 写死 120 的时候，第二行会被框底裁掉。
        // 末尾多留 46：底下一行放“上一步”按钮，不然按钮会压在正文上。
        int lines = CountLines(p.body, w - 56f, bodySize);
        float bodyH = lines * lineH;
        float h = 52f + bodyH + 46f;

        var r = new Rect((Screen.width - w) * .5f, Screen.height - h - 78f, w, h);
        Panel(r);
        GUI.Label(new Rect(r.x + 26f, r.y + 13f, r.width - 52f, 34f), p.title ?? "", Style(titleSize, TextAnchor.MiddleLeft, new Color(.62f, .86f, 1f)));
        GUI.Label(new Rect(r.x + 26f, r.y + 48f, r.width - 52f, bodyH + 4f), p.body ?? "", Style(bodySize, TextAnchor.UpperLeft, new Color(.94f, .95f, .97f)));
        DrawBottomRow(r, r.y + 52f + bodyH, 24f, 34f, false);
    }

    /// <summary>
    /// 估算一段文字在给定宽度下要占几行：中文按一个字号宽、西文按 0.56 个字号宽累加。
    /// 不用真实字体度量，只用来算面板高度，估大一点不会裁字。
    /// </summary>
    static int CountLines(string text, float width, int size)
    {
        if (string.IsNullOrEmpty(text)) return 1;
        int total = 0;
        foreach (var para in text.Split('\n'))
        {
            float x = 0f;
            int n = 1;
            foreach (char c in para)
            {
                float cw = c > 0x2E80 ? size : size * .56f;
                if (x + cw > width) { n++; x = cw; }
                else x += cw;
            }
            total += n;
        }
        return Mathf.Max(1, total);
    }

    void DrawAsk(Page p)
    {
        float w = Mathf.Min(Screen.width * .80f, 980f);
        float bodyH = 92f;
        float btnH = 62f, gap = 16f;
        float h = 96f + bodyH + p.options.Length * (btnH + gap) + 60f;
        var r = new Rect((Screen.width - w) * .5f, (Screen.height - h) * .5f, w, h);
        Panel(r);
        float pad = 40f;
        GUI.Label(new Rect(r.x + pad, r.y + 26f, r.width - pad * 2f, 44f), p.title ?? "", Style(27, TextAnchor.MiddleCenter, new Color(.66f, .88f, 1f)));
        GUI.Label(new Rect(r.x + pad, r.y + 76f, r.width - pad * 2f, bodyH), p.body ?? "", Style(23, TextAnchor.MiddleCenter, new Color(.95f, .96f, .98f)));

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
        for (int i = 0; i < p.options.Length; i++)
        {
            var br = new Rect(r.x + pad, y, r.width - pad * 2f, btnH);
            bool hover = br.Contains(Event.current.mousePosition);
            Fill(br, hover ? new Color(.12f, .30f, .46f, .95f) : new Color(.06f, .13f, .20f, .92f));
            var edge = hover ? new Color(.55f, .85f, 1f) : new Color(.30f, .52f, .66f);
            Fill(new Rect(br.x, br.y, br.width, 2f), edge);
            Fill(new Rect(br.x, br.yMax - 2f, br.width, 2f), edge);
            Fill(new Rect(br.x, br.y, 2f, br.height), edge);
            Fill(new Rect(br.xMax - 2f, br.y, 2f, br.height), edge);
            if (GUI.Button(br, "① ② ③ ④ ⑤ ⑥ "[i * 2] + "  " + p.options[i], btnStyle))
            {
                var answer = current.answer;
                Advance();
                answer?.Invoke(i);
            }
            y += btnH + gap;
        }
        DrawBottomRow(r, r.yMax - 50f, pad, 34f, false);
    }

    /// <summary>
    /// 回看专用的只读页（问答页用）：标题 + 正文 + 选项文字，全部画成死的，
    /// 避免玩家回看时不小心又提交一次答案。
    /// </summary>
    void DrawPlain(Page p)
    {
        float w = Mathf.Min(Screen.width * .78f, 940f);
        float h = 250f;
        var r = new Rect((Screen.width - w) * .5f, (Screen.height - h) * .5f, w, h);
        Panel(r);
        float pad = 34f;
        GUI.Label(new Rect(r.x + pad, r.y + 26f, r.width - pad * 2f, 46f), p.title ?? "", Style(29, TextAnchor.MiddleCenter, new Color(.62f, .86f, 1f)));
        string body = p.body ?? "";
        if (p.options != null)
        {
            var sb = new System.Text.StringBuilder(body);
            sb.Append("\n\n");
            for (int i = 0; i < p.options.Length; i++) sb.Append("① ② ③ ④ ⑤ ⑥ "[i * 2]).Append("  ").Append(p.options[i]).Append(i + 1 < p.options.Length ? "\n" : "");
            body = sb.ToString();
        }
        GUI.Label(new Rect(r.x + pad, r.y + 82f, r.width - pad * 2f, h - 140f), body, Style(24, TextAnchor.UpperCenter, new Color(.95f, .96f, .98f)));
        DrawBottomRow(r, r.yMax - 52f, pad, 34f, false);
    }

    void DrawLetter(Page p)
    {
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, .82f));

        float h = Mathf.Min(Screen.height * .82f, 760f);
        float w = h * .74f;
        var r = new Rect((Screen.width - w) * .5f, (Screen.height - h) * .5f, w, h);

        if (letterPaper) GUI.DrawTexture(r, letterPaper, ScaleMode.ScaleAndCrop, true);
        else Fill(r, new Color(.94f, .90f, .80f, 1f));

        float pad = w * .10f;
        GUI.Label(new Rect(r.x + pad, r.y + h * .16f, w - pad * 2f, 60f),
            p.title ?? "", Style(Mathf.RoundToInt(h * .052f), TextAnchor.MiddleCenter, new Color(.17f, .12f, .09f)));
        GUI.Label(new Rect(r.x + pad, r.y + h * .30f, w - pad * 2f, h * .44f),
            p.body ?? "", Style(Mathf.RoundToInt(h * .042f), TextAnchor.UpperCenter, new Color(.11f, .08f, .06f)));
        // 信纸底色是浅的，按钮走“无底色 + 深色字”，不然白字压在白纸上看不见。
        DrawBottomRow(r, r.y + h * .82f - 6f, pad, Mathf.Clamp(h * .05f, 32f, 40f), true);
    }

    void DrawEnding(Page p, bool instant)
    {
        Fill(new Rect(0, 0, Screen.width, Screen.height), Color.black);

        float w = Mathf.Min(Screen.width * .70f, 880f);
        float x = (Screen.width - w) * .5f;
        float y = Screen.height * .28f;

        // 标题也淡入，不要一下就砸在黑幕上
        float head = instant ? 1f : Mathf.Clamp01((Time.unscaledTime - shownAt) / .55f);
        GUI.Label(new Rect(x, y, w, 50f), p.title ?? "", Style(26, TextAnchor.MiddleCenter, new Color(.78f, .80f, .84f, head)));
        y += 74f;

        int total = p.lines == null ? 0 : p.lines.Length;
        int shownLines = instant ? total : typedLine;              // 回看时一次全显示，不用再等打字
        for (int i = 0; i < shownLines && i < total; i++)
        {
            GUI.Label(new Rect(x, y, w, 58f), p.lines[i], Style(25, TextAnchor.MiddleCenter, new Color(.96f, .96f, .96f)));
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
