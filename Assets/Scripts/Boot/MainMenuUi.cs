using System;
using UnityEngine;

/// <summary>
/// 序幕视频之后的开始界面。
///
/// 主菜单直接用玩家给的那张图当底图（图里已经画好了标题和四个菜单项），
/// 这里只在文字位置盖透明热区，鼠标移上去时加一层很淡的暖金底光 + 左侧一道细金线，
/// 这样既不会和底图文字重影，也不破坏画面的质感。
///
/// 设置 / 帮助用同一套暖金面板（和主菜单同一套配色，而不是游戏内那个冷蓝面板）。
/// </summary>
[DisallowMultipleComponent]
public sealed class MainMenuUi : MonoBehaviour
{
    public Texture2D Background;
    public Font UiFont;
    public Action OnStartGame;
    public Action OnQuit;

    enum View { Main, Settings, Help }
    View view = View.Main;

    // ---------------- 暖金配色 ----------------
    static readonly Color Gold = new Color(.95f, .79f, .45f, 1f);
    static readonly Color LineColor = new Color(.72f, .60f, .38f, .28f);
    static readonly Color PanelFill = new Color(.042f, .037f, .031f, .962f);
    static readonly Color PanelEdge = new Color(.64f, .52f, .32f, .58f);
    static readonly Color TextMain = new Color(.94f, .91f, .85f, 1f);
    static readonly Color TextDim = new Color(.66f, .62f, .55f, .95f);
    static readonly Color BtnFill = new Color(.085f, .072f, .056f, .96f);
    static readonly Color BtnHover = new Color(.165f, .135f, .092f, .98f);
    static readonly Color TrackFill = new Color(.14f, .12f, .09f, .95f);

    // 底图 1920×1080 上量出来的菜单位置（占屏幕的比例）
    static readonly float[] ItemY = { .385f, .492f, .596f, .699f };
    const float ItemCenterX = .311f;
    const float ItemWidth = .152f;
    const float ItemHeight = .078f;

    float volume = .60f, sensitivity = 1.60f;

    const string VolKey = "opt_bgm_volume";
    const string SensKey = "opt_mouse_sensitivity";

    Font font;
    Texture2D white;
    Matrix4x4 savedMatrix;

    void Awake()
    {
        font = UiFont ? UiFont : UnityEngine.Font.CreateDynamicFontFromOSFont(
            new[] { "Microsoft YaHei", "SimHei", "Arial" }, 24);
        white = L2.SolidTexture(Color.white);

        volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolKey, .60f));
        sensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(SensKey, 1.60f), .1f, 4.5f);
        AudioListener.volume = volume;
    }

    void OnDestroy()
    {
        if (white) Destroy(white);
    }

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape)) return;
        if (view == View.Main) return;
        view = View.Main;
    }

    // ---------------------------------------------------------------- 绘制入口

    void OnGUI()
    {
        GUI.depth = -3000;
        DrawBackground();

        if (view == View.Main) DrawMain();
        else
        {
            DrawPanelBackdrop();
            if (view == View.Settings) DrawSettings();
            else DrawHelp();
            GUI.matrix = savedMatrix;          // 面板用的是设计画布缩放，画完必须还原
        }
    }

    void DrawBackground()
    {
        var old = GUI.color;
        if (Background)
        {
            GUI.color = Color.white;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Background, ScaleMode.ScaleAndCrop);
        }
        else
        {
            GUI.color = new Color(.03f, .03f, .035f, 1f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), white);
        }
        GUI.color = old;
    }

    // ---------------------------------------------------------------- 主菜单

    void DrawMain()
    {
        for (int i = 0; i < ItemY.Length; i++)
        {
            var rect = new Rect(
                Screen.width * (ItemCenterX - ItemWidth * .5f),
                Screen.height * (ItemY[i] - ItemHeight * .5f),
                Screen.width * ItemWidth,
                Screen.height * ItemHeight);

            bool hover = rect.Contains(Event.current.mousePosition);

            if (hover)
            {
                // 文字后面一层极淡的暖金，像有人在灯下把这一行照亮了一点。
                Fill(rect, new Color(1f, .84f, .52f, .075f));

                // 左侧一道细金线，指示当前选中的是哪一行。
                float lh = rect.height * .52f;
                Fill(new Rect(rect.x - Screen.width * .012f, rect.y + (rect.height - lh) * .5f,
                    2f, lh), new Color(1f, .84f, .52f, .85f));
            }

            if (GUI.Button(rect, GUIContent.none, GUIStyle.none))
            {
                if (i == 0) { CloseMenuAndStart(); }
                else if (i == 1) view = View.Settings;
                else if (i == 2) view = View.Help;
                else OnQuit?.Invoke();
            }
        }
    }

    void CloseMenuAndStart()
    {
        OnStartGame?.Invoke();
    }

    // ---------------------------------------------------------------- 面板外壳

    GUIStyle Style(int size, TextAnchor anchor, Color color)
        => new GUIStyle(GUI.skin.label)
        {
            font = font,
            fontSize = size,
            alignment = anchor,
            wordWrap = true,
            normal = { textColor = color },
        };

    void Fill(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, white);
        GUI.color = old;
    }

    void Outline(Rect r, Color c, float t = 2f)
    {
        Fill(new Rect(r.x, r.y, r.width, t), c);
        Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
        Fill(new Rect(r.x, r.y, t, r.height), c);
        Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
    }

    void DrawPanelBackdrop()
    {
        Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, .72f));
    }

    /// <summary>980×640 的设计画布，按屏幕等比缩放；返回面板矩形（设计坐标）。</summary>
    Rect BeginPanel()
    {
        const float BW = 980f, BH = 640f;
        float s = Mathf.Min(Screen.width / BW, Screen.height / BH) * .97f;
        savedMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(
            new Vector3((Screen.width - BW * s) * .5f, (Screen.height - BH * s) * .5f, 0f),
            Quaternion.identity, new Vector3(s, s, 1f));

        var panel = new Rect((BW - 900f) * .5f, (BH - 580f) * .5f, 900f, 580f);
        Fill(panel, PanelFill);
        Outline(panel, PanelEdge);
        // 顶部一条暖金细光，和主菜单的灯色呼应
        Fill(new Rect(panel.x, panel.y, panel.width, 2.5f), new Color(.90f, .72f, .40f, .62f));
        return panel;
    }

    void DrawPanelTitle(Rect panel, string title)
    {
        float pad = 44f;
        float x = panel.x + pad, inner = panel.width - pad * 2f;
        GUI.Label(new Rect(x, panel.y + 30f, inner, 52f), title, Style(36, TextAnchor.MiddleLeft, Gold));
        GUI.Label(new Rect(x, panel.y + 36f, inner, 40f), "Esc 返回", Style(18, TextAnchor.MiddleRight, TextDim));
        Fill(new Rect(x, panel.y + 92f, inner, 1f), LineColor);
    }

    // ---------------------------------------------------------------- 设置

    void DrawSettings()
    {
        var panel = BeginPanel();
        DrawPanelTitle(panel, "设  置");

        float pad = 44f;
        float x = panel.x + pad, inner = panel.width - pad * 2f;
        float y = panel.y + 122f;

        y = DrawSlider(x, y, inner, "背景音量", ref volume, 0f, 1f,
            Mathf.RoundToInt(volume * 100f) + "%", v =>
            {
                volume = v;
                AudioListener.volume = volume;
                PlayerPrefs.SetFloat(VolKey, volume);
                PlayerPrefs.Save();
            });

        y = DrawSlider(x, y + 12f, inner, "视角灵敏度", ref sensitivity, .3f, 4.5f,
            sensitivity.ToString("F1"), v =>
            {
                sensitivity = v;
                foreach (var fpv in FindObjectsOfType<FirstPersonView>())
                    if (fpv) fpv.mouseSensitivity = sensitivity;
                PlayerPrefs.SetFloat(SensKey, sensitivity);
                PlayerPrefs.Save();
            });

        y += 34f;
        Fill(new Rect(x, y, inner, 1f), LineColor);

        var back = new Rect(x, y + 36f, inner, 52f);
        if (Button(back, "返 回 主 菜 单", true)) view = View.Main;

        GUI.Label(new Rect(x, panel.yMax - 56f, inner, 30f),
            "设置会保存在本机；进入游戏后按 Esc 还能调出设置、保存进度。",
            Style(15, TextAnchor.MiddleCenter, new Color(.55f, .50f, .43f)));
    }

    float DrawSlider(float x, float y, float width, string label, ref float value, float min, float max,
        string readout, Action<float> onChanged)
    {
        GUI.Label(new Rect(x, y, 240f, 30f), label, Style(21, TextAnchor.MiddleLeft, TextMain));
        GUI.Label(new Rect(x + width - 90f, y, 90f, 30f), readout, Style(19, TextAnchor.MiddleRight, Gold));
        y += 34f;

        var track = new Rect(x, y + 9f, width, 6f);
        Fill(track, TrackFill);

        float t = Mathf.InverseLerp(min, max, value);
        Fill(new Rect(track.x, track.y, track.width * t, track.height), new Color(.76f, .60f, .34f, 1f));

        var knob = new Rect(track.x + track.width * t - 7f, track.y - 6f, 14f, 18f);
        Fill(knob, new Color(1f, .93f, .76f, 1f));
        Outline(knob, new Color(.62f, .48f, .26f, .9f), 1f);

        var hit = new Rect(track.x, track.y - 12f, track.width, 30f);
        var e = Event.current;
        if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && hit.Contains(e.mousePosition))
        {
            float v = Mathf.Lerp(min, max, Mathf.Clamp01(Mathf.InverseLerp(track.x, track.xMax, e.mousePosition.x)));
            if (!Mathf.Approximately(v, value))
            {
                value = v;
                onChanged(v);
            }
            e.Use();
        }

        return y + 40f;
    }

    // ---------------------------------------------------------------- 帮助

    void DrawHelp()
    {
        var panel = BeginPanel();
        DrawPanelTitle(panel, "帮  助");

        var rows = new[]
        {
            new[] { "WASD", "移动" },
            new[] { "鼠标", "转向" },
            new[] { "左Ctrl", "蹲下" },
            new[] { "E", "交互" },
            new[] { "G", "丢弃道具" },
            new[] { "Q", "推动道具" },
        };

        const float RowH = 56f;
        const float BlockW = 470f;
        float top = panel.y + 112f;
        float bottom = panel.yMax - 138f;      // 再往上留出底部“返回”按钮的位置
        // 整块内容在标题线以下的区域里垂直居中：不偏上、也不贴底。
        float startY = (top + bottom) * .5f - rows.Length * RowH * .5f;
        float x = panel.x + (panel.width - BlockW) * .5f;

        for (int i = 0; i < rows.Length; i++)
        {
            float y = startY + i * RowH;
            GUI.Label(new Rect(x, y, 150f, RowH), rows[i][0], Style(25, TextAnchor.MiddleRight, Gold));
            GUI.Label(new Rect(x + 176f, y, BlockW - 176f, RowH), rows[i][1], Style(24, TextAnchor.MiddleLeft, TextMain));

            if (i < rows.Length - 1)
                Fill(new Rect(x + 40f, y + RowH - 1f, BlockW - 80f, 1f), new Color(.72f, .60f, .38f, .12f));
        }

        var back = new Rect(panel.x + 44f, panel.yMax - 118f, panel.width - 88f, 50f);
        if (Button(back, "返 回 主 菜 单", false)) view = View.Main;
    }

    // ---------------------------------------------------------------- 控件

    bool Button(Rect r, string label, bool primary)
    {
        bool hover = r.Contains(Event.current.mousePosition);
        Fill(r, hover ? BtnHover : BtnFill);
        Outline(r, hover ? Gold : (primary ? new Color(.64f, .52f, .32f, .78f) : new Color(.45f, .38f, .27f, .55f)));
        if (primary && !hover)
            Fill(new Rect(r.x, r.y, 3f, r.height), new Color(.86f, .68f, .38f, 1f));

        return GUI.Button(r, label, Style(21, TextAnchor.MiddleCenter, hover ? Color.white : TextMain));
    }
}
