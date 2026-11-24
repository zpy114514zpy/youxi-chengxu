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
    // 第一关那套“石板 + 金边”提示框贴图，第二关的提示框直接沿用，保持两关观感一致。
    Texture2D panelTex;
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
        // 第一关“提示”那一页用的就是这张：左上角挂锁 + 铜边 + 深石板，右下角一个 ▶。
        panelTex = Resources.Load<Texture2D>("StoryUI/guide-clean");
        if (!panelTex) Debug.LogWarning("[第二关] 找不到提示框贴图 Resources/StoryUI/guide-clean，退回纯色面板");
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

    // 第一关那块石板是深灰蓝底 + 细金边，标题和正文都用白字最协调；这里只是把纯白压得柔和一点。
    static readonly Color TitleColor = new Color(.97f, .95f, .89f);
    static readonly Color BodyColor = new Color(.90f, .91f, .92f);

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

    // guide-clean 这张图里：0~50 行是挂锁 + 上边框，172~204 行是下边框 + 角饰，中间 51~171 是石板。
    // 画的时候上下两条按宽度等比缩放（锁和四角不变形），中间那条随面板高度拉伸。
    const float TexTopBand = 50f / 204f;
    const float TexBottomBand = 33f / 204f;

    /// <summary>一页提示框摆好之后的几个矩形。</summary>
    struct Frames
    {
        public Rect panel, title, rule, body, row;
        public bool hasRule;
        public float scale;
        public int titleSize, bodySize;
    }

    /// <summary>
    /// 量好这一页要多大：宽度固定，高度按正文实际行数（CalcHeight 真量，不靠估）撑开，
    /// 但不会比贴图原比例更矮，所以短提示条和高选项框是同一套框。
    /// 版式固定为：上边框 → 标题 → 细金线 → 正文 → 底部一行 → 下边框。
    /// </summary>
    Frames Layout(string title, string body, float extraH, bool centered,
        float widthFactor = .86f, float maxW = 1040f)
    {
        float w = Mathf.Min(Screen.width * widthFactor, maxW);
        float sc = Mathf.Clamp(w / 1000f, .72f, 1f);
        int titleSize = Mathf.RoundToInt(27f * sc);
        int bodySize = Mathf.RoundToInt(22f * sc);
        float padX = w * .115f;                    // 左右留白：正文别贴着金边
        float bodyW = w - padX * 2f;

        float titleH = Mathf.Round(titleSize * 1.36f);
        float titleGap = Mathf.Round(16f * sc);    // 标题 → 分隔线 → 正文
        float bodyH = string.IsNullOrEmpty(body)
            ? 0f
            : Style(bodySize, TextAnchor.MiddleCenter, BodyColor).CalcHeight(new GUIContent(body), bodyW);
        float rowH = Mathf.Round(38f * sc);

        float topBand = panelTex ? TexTopBand * panelTex.height * (w / panelTex.width) : w * .062f;
        float botBand = panelTex ? TexBottomBand * panelTex.height * (w / panelTex.width) : w * .041f;

        float need = topBand + 14f + titleH + titleGap + bodyH + 16f
                     + (extraH > 0f ? 12f + extraH : 0f) + 10f + rowH + 10f + botBand;
        float natural = panelTex ? w * panelTex.height / panelTex.width : w * .258f;
        float h = Mathf.Min(Mathf.Max(need, natural), Screen.height * .94f);

        float y = centered
            ? Mathf.Max(12f, (Screen.height - h) * .5f)
            : Mathf.Clamp(Screen.height * .66f - h * .5f, 12f, Mathf.Max(12f, Screen.height - h - 12f));
        var r = new Rect((Screen.width - w) * .5f, y, w, h);

        // 标题 + 正文这一块在“上边框到下边那一行”之间垂直居中，短提示不会贴着顶边。
        float contentTop = r.y + topBand + 14f;
        float contentBot = r.yMax - botBand - rowH - 10f;
        float blockH = titleH + titleGap + bodyH + (extraH > 0f ? 12f + extraH : 0f);
        float t = contentTop + Mathf.Max(0f, (contentBot - contentTop - blockH) * .5f);

        var f = new Frames { panel = r, scale = sc, titleSize = titleSize, bodySize = bodySize };
        f.title = new Rect(r.x + padX, t, bodyW, titleH);
        f.hasRule = bodyH > 0f;
        // 细金线：只占正文宽度的一半，起分隔作用又不抢戏。
        f.rule = new Rect(r.x + padX + bodyW * .25f, t + titleH + titleGap * .5f, bodyW * .5f, 1f);
        f.body = new Rect(r.x + padX, t + titleH + titleGap, bodyW, bodyH);
        f.row = new Rect(r.x + padX, r.yMax - botBand - rowH - 8f, bodyW, rowH);
        return f;
    }

    static readonly Color RuleColor = new Color(.86f, .73f, .42f, .30f);

    /// <summary>标题 + 分隔线 + 正文，三页共用同一套版式，省得各画各的。</summary>
    void DrawTextBlock(Frames f, string title, string body)
    {
        GUI.Label(f.title, title ?? "", Style(f.titleSize, TextAnchor.MiddleCenter, TitleColor));
        if (f.hasRule) Fill(f.rule, RuleColor);
        if (f.body.height > 0f)
            GUI.Label(f.body, body ?? "", Style(f.bodySize, TextAnchor.MiddleCenter, BodyColor));
    }

    /// <summary>整张面板：上（锁+边框）/ 中（石板，拉伸）/ 下（边框+角饰）三片拼。</summary>
    void DrawPanel(Rect r)
    {
        if (!panelTex) { FallbackPanel(r); return; }

        float k = r.width / panelTex.width;
        float top = TexTopBand * panelTex.height * k;
        float bot = TexBottomBand * panelTex.height * k;
        if (top + bot > r.height * .8f)                    // 面板特别矮时上下按比例缩，别把中间挤没
        {
            float m = r.height * .8f / (top + bot);
            top *= m; bot *= m;
        }
        float mid = Mathf.Max(1f, r.height - top - bot);

        Slice(new Rect(r.x, r.y, r.width, top), 1f - TexTopBand, 1f);
        Slice(new Rect(r.x, r.y + top, r.width, mid), TexBottomBand, 1f - TexTopBand);
        Slice(new Rect(r.x, r.yMax - bot, r.width, bot), 0f, TexBottomBand);
    }

    /// <summary>画贴图的一条横带。v 从贴图底部算（GUI 纹理坐标的原点在左下）。</summary>
    void Slice(Rect dst, float vBottom, float vTop)
    {
        GUI.DrawTextureWithTexCoords(dst, panelTex, new Rect(0f, vBottom, 1f, vTop - vBottom), true);
    }

    /// <summary>没找到第一关的框贴图时的兜底：还是原来的半透明深色板 + 细亮边。</summary>
    void FallbackPanel(Rect r)
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

    void DrawCenter(Page p) => DrawNotice(p, false);

    void DrawNote(Page p) => DrawNotice(p, true);

    /// <summary>提示 / 叙事页：标题居中、正文居中，底部一行“上一步 + 空格继续”。</summary>
    void DrawNotice(Page p, bool lower)
    {
        // 目标提示（下三分之一）收窄，短句一行读完不横跨整屏；叙事页宽一些，长句不用挤成好几行。
        var f = lower ? Layout(p.title, p.body, 0f, false, .74f, 860f)
                      : Layout(p.title, p.body, 0f, true, .82f, 980f);
        DrawPanel(f.panel);
        DrawTextBlock(f, p.title, p.body);
        DrawBottomRow(f);
    }

    void DrawAsk(Page p)
    {
        float w = Mathf.Min(Screen.width * .86f, 1040f);
        float sc = Mathf.Clamp(w / 1000f, .72f, 1f);
        float btnH = Mathf.Round(58f * sc);
        float gap = Mathf.Round(12f * sc);
        int n = p.options == null ? 0 : p.options.Length;
        float extra = n * (btnH + gap);

        var f = Layout(p.title, p.body, extra, true);
        DrawPanel(f.panel);
        DrawTextBlock(f, p.title, p.body);

        var btnStyle = new GUIStyle(GUI.skin.label)
        {
            font = font,
            fontSize = Mathf.RoundToInt(21f * sc),
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            border = new RectOffset(0, 0, 0, 0)
        };
        btnStyle.normal.textColor = Color.white;
        btnStyle.hover.textColor = new Color(1f, .92f, .62f);
        btnStyle.active.textColor = new Color(1f, .82f, .35f);

        float pad = f.panel.width * .115f;
        float y = f.body.yMax + 10f;
        for (int i = 0; i < n; i++)
        {
            var br = new Rect(f.panel.x + pad, y, f.panel.width - pad * 2f, btnH);
            bool hover = br.Contains(Event.current.mousePosition);
            Fill(br, hover ? new Color(.20f, .17f, .11f, .96f) : new Color(.05f, .06f, .07f, .90f));
            var edge = hover ? new Color(.94f, .78f, .40f) : new Color(.56f, .47f, .30f);
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
        // 问答页空格是无效的，底部别再写“空格继续”误导玩家，直接说清楚要选一个。
        DrawBottomRow(f, n > 0 ? "点上面的选项回答" : null);
    }

    /// <summary>
    /// 回看专用的只读页（问答页用）：标题 + 正文 + 选项文字，全部画成死的，
    /// 避免玩家回看时不小心又提交一次答案。
    /// </summary>
    void DrawPlain(Page p)
    {
        string body = p.body ?? "";
        if (p.options != null)
        {
            var sb = new System.Text.StringBuilder(body);
            sb.Append("\n\n");
            for (int i = 0; i < p.options.Length; i++) sb.Append("① ② ③ ④ ⑤ ⑥ "[i * 2]).Append("  ").Append(p.options[i]).Append(i + 1 < p.options.Length ? "\n" : "");
            body = sb.ToString();
        }

        var f = Layout(p.title, body, 0f, true, .82f, 980f);
        DrawPanel(f.panel);
        DrawTextBlock(f, p.title, body);
        DrawBottomRow(f);
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
        DrawBottomRowOnPaper(new Rect(r.x + pad, r.y + h * .82f - 6f, w - pad * 2f, Mathf.Clamp(h * .05f, 32f, 40f)), 1f);
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
        if (!instant && typedLine < total && typedCount > 0)
        {
            string line = p.lines[typedLine] ?? "";
            string part = line.Substring(0, Mathf.Min(typedCount, line.Length));
            float fade = Mathf.Clamp01(typedCount / 6f);
            GUI.Label(new Rect(x, y, w, 58f), part, Style(25, TextAnchor.MiddleCenter, new Color(.96f, .96f, .96f, fade)));
        }

        string hint = instant ? "空格返回" : "空格继续";
        if (instant || AllTyped && total > 0)
            GUI.Label(new Rect(x, Screen.height - 58f, w, 34f), hint, Style(18, TextAnchor.MiddleCenter, new Color(.62f, .64f, .68f)));
    }

    // ---------------- 左下角“上一步” ----------------

    /// <summary>
    /// 面板底部一行：左下角“上一步”（可点击回看，能连点继续往前翻），右下角“空格继续 / 空格返回”。
    /// </summary>
    void DrawBottomRow(Frames f, string hintOverride = null)
    {
        float pillH = Mathf.Min(f.row.height, Mathf.Round(34f * f.scale));
        float pillW = Mathf.Min(Mathf.Round(150f * f.scale), f.row.width * .34f);
        var backRect = new Rect(f.row.x, f.row.y + (f.row.height - pillH) * .5f, pillW, pillH);
        int hintSize = Mathf.RoundToInt(18f * f.scale);

        if (BackButton(backRect, hintSize, backSteps.Count > 0, false)) pendingBack = true;

        var hintColor = new Color(.86f, .85f, .80f, .92f);
        string hint = hintOverride ?? (reviewing != null ? "空格返回" : "空格继续");
        GUI.Label(new Rect(f.row.x + pillW + 8f, f.row.y, f.row.width - pillW - 8f, f.row.height),
            hint, Style(hintSize, TextAnchor.MiddleRight, hintColor));
    }

    /// <summary>信纸页的底部一行：纸是浅色的，用深棕字，不要白底药丸。</summary>
    void DrawBottomRowOnPaper(Rect row, float sc)
    {
        float pillH = Mathf.Min(row.height, Mathf.Round(34f * sc));
        float pillW = Mathf.Min(Mathf.Round(150f * sc), row.width * .34f);
        var backRect = new Rect(row.x, row.y + (row.height - pillH) * .5f, pillW, pillH);
        int hintSize = Mathf.RoundToInt(18f * sc);

        if (BackButton(backRect, hintSize, backSteps.Count > 0, true)) pendingBack = true;

        string hint = reviewing != null ? "空格返回" : "空格合上";
        GUI.Label(new Rect(row.x + pillW + 8f, row.y, row.width - pillW - 8f, row.height),
            hint, Style(hintSize, TextAnchor.MiddleRight, new Color(.34f, .26f, .18f, .85f)));
    }

    /// <summary>“上一步”按钮：没有历史时是灰的、点不动；有历史时可点，悬停/按下会变色。</summary>
    bool BackButton(Rect rect, int size, bool enabled, bool onPaper)
    {
        var style = new GUIStyle(GUI.skin.label)
        {
            font = font,
            fontSize = size,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = false,
            padding = new RectOffset(0, 0, 0, 0),
            margin = new RectOffset(0, 0, 0, 0)
        };

        if (onPaper)
        {
            // 信纸是浅色，用深棕字 + 一条淡底线，不要白底药丸。
            style.normal.textColor = enabled ? new Color(.30f, .20f, .12f, .95f) : new Color(.42f, .36f, .30f, .5f);
            style.hover.textColor = new Color(.62f, .28f, .10f, 1f);
            style.active.textColor = new Color(.78f, .36f, .10f, 1f);
            style.onNormal.textColor = style.normal.textColor;
            style.onHover.textColor = style.hover.textColor;
            style.onActive.textColor = style.active.textColor;
            if (!enabled) { GUI.Label(rect, "←  上一步", style); return false; }
            return GUI.Button(rect, "←  上一步", style);
        }

        EnsurePill();
        style.border = new RectOffset(PillBorder, PillBorder, PillBorder, PillBorder);
        if (!enabled)
        {
            style.normal.background = pillOff;
            style.normal.textColor = new Color(1f, 1f, 1f, .34f);
            GUI.Label(rect, "←  上一步", style);
            return false;
        }
        style.normal.background = pillOn;
        style.hover.background = pillHover;
        style.active.background = pillHover;
        style.onNormal.background = pillOn;
        style.onHover.background = pillHover;
        style.onActive.background = pillHover;
        style.normal.textColor = new Color(1f, 1f, 1f, .94f);
        style.hover.textColor = new Color(1f, .9f, .55f, 1f);
        style.active.textColor = new Color(1f, .82f, .35f, 1f);
        style.onNormal.textColor = style.normal.textColor;
        style.onHover.textColor = style.hover.textColor;
        style.onActive.textColor = style.active.textColor;
        return GUI.Button(rect, "←  上一步", style);
    }

    // 圆角药丸底图：横向拉伸靠 9 宫格边框，所以只用一张 64×64 的小图，不会把圆角拉糊。
    const int PillBorder = 14;

    void EnsurePill()
    {
        if (pillOn) return;
        pillOn = PillTexture(.13f);
        pillHover = PillTexture(.26f);
        pillOff = PillTexture(.05f);
    }

    static Texture2D PillTexture(float alpha)
    {
        const int n = 64, r = 14;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false)
        { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[n * n];
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float qx = Mathf.Abs(x + .5f - n * .5f) - (n * .5f - r), qy = Mathf.Abs(y + .5f - n * .5f) - (n * .5f - r);
                float d = Mathf.Sqrt(Mathf.Max(qx, 0f) * Mathf.Max(qx, 0f) + Mathf.Max(qy, 0f) * Mathf.Max(qy, 0f))
                    + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                float cover = Mathf.Clamp01(.5f - d);        // 边缘一个像素做抗锯齿
                pixels[y * n + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(cover * alpha * 255f));
            }
        tex.SetPixels32(pixels);
        tex.Apply();
        return tex;
    }

    /// <summary>给镜子用的成人剪影贴图（黑幕之外也可以拿来当镜面层）。</summary>
    public Texture2D AdultFigure => adultFigure;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (font) Destroy(font);
        if (white) Destroy(white);
        if (adultFigure) Destroy(adultFigure);
        if (pillOn) Destroy(pillOn);
        if (pillHover) Destroy(pillHover);
        if (pillOff) Destroy(pillOff);
        if (blockerSet) UiInputBlocker.Active = false;
    }
}
