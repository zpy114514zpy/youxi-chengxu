using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 全局设置/暂停界面（Esc 打开）。
/// 冷色调：深蓝黑底、钢蓝描边、淡青光字 —— 和第一关的暖黄、第二关的冷灰都能搭上。
///
/// 功能：
///   · 保存当前进度并退出（进度按 4 格循环自动往后写，界面上不再列存档格、也不提供读档）
///   · 背景音量（控制全局 AudioListener 音量 + 第二关的房间底噪）
///   · 第一人称灵敏度（直接写 FirstPersonView.mouseSensitivity）
/// </summary>
[DefaultExecutionOrder(200)]
public sealed class GameMenu : MonoBehaviour
{
    public static GameMenu Instance { get; private set; }

    // ---------------- 配色 ----------------
    static readonly Color Ink = new Color(.015f, .028f, .045f, .92f);
    static readonly Color PanelFill = new Color(.030f, .060f, .095f, .965f);
    static readonly Color PanelEdge = new Color(.34f, .62f, .86f, .55f);
    static readonly Color Line = new Color(.30f, .50f, .70f, .30f);
    static readonly Color TextMain = new Color(.88f, .93f, .98f, 1f);
    static readonly Color TextDim = new Color(.62f, .72f, .82f, .95f);
    static readonly Color Accent = new Color(.52f, .84f, 1f, 1f);
    static readonly Color AccentSoft = new Color(.30f, .62f, .90f, 1f);
    static readonly Color BtnFill = new Color(.055f, .115f, .170f, .96f);
    static readonly Color BtnHover = new Color(.085f, .195f, .285f, .98f);

    const string VolKey = "opt_bgm_volume";
    const string SensKey = "opt_mouse_sensitivity";

    bool open;
    float volume = .60f, sensitivity = 1.60f;
    string toast = "";
    float toastUntil;

    Font font;
    Texture2D white;
    AudioSource ambience;
    AudioClip roomTone;
    string ambienceScene = "";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance) return;
        var go = new GameObject("设置界面");
        DontDestroyOnLoad(go);
        go.AddComponent<GameMenu>();
    }

    void Awake()
    {
        Instance = this;
        font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 24);
        white = L2.SolidTexture(Color.white);

        volume = Mathf.Clamp01(PlayerPrefs.GetFloat(VolKey, .60f));
        sensitivity = Mathf.Clamp(PlayerPrefs.GetFloat(SensKey, 1.60f), .1f, 4.5f);
        AudioListener.volume = volume;

        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
        if (font) Destroy(font);
        if (white) Destroy(white);
        if (roomTone) Destroy(roomTone);
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ApplySensitivity();
        EnsureAmbience(scene.name);
    }

    // ---------------- 与游戏对接 ----------------

    void ApplySensitivity()
    {
        foreach (var fpv in FindObjectsOfType<FirstPersonView>())
            if (fpv) fpv.mouseSensitivity = sensitivity;
    }

    /// <summary>第二关有一点很轻的房间底噪，音量滑杆才有得可调；第一关保持原本的安静。</summary>
    void EnsureAmbience(string sceneName)
    {
        if (ambienceScene == sceneName) return;
        ambienceScene = sceneName;

        if (ambience) { Destroy(ambience.gameObject); ambience = null; }
        if (sceneName != "Level 2") return;

        if (!roomTone) roomTone = MakeRoomTone();
        var go = new GameObject("房间底噪");
        go.transform.SetParent(transform, false);
        ambience = go.AddComponent<AudioSource>();
        ambience.clip = roomTone;
        ambience.loop = true;
        ambience.playOnAwake = true;
        ambience.spatialBlend = 0f;
        ambience.volume = .16f;
        ambience.Play();
    }

    /// <summary>一段 8 秒、首尾交叉淡化的低频风声，循环起来听不出接缝。</summary>
    static AudioClip MakeRoomTone()
    {
        const int rate = 44100;
        const int len = rate * 8;
        var data = new float[len];
        var rnd = new System.Random(11);
        float lp1 = 0f, lp2 = 0f;
        for (int i = 0; i < len; i++)
        {
            float n = (float)(rnd.NextDouble() * 2.0 - 1.0);
            lp1 += (n - lp1) * .0022f;          // 两层低通，滤成“呼呼”的气流
            lp2 += (lp1 - lp2) * .010f;
            float env = .72f + .28f * Mathf.Sin(i / (float)rate * .37f);
            data[i] = Mathf.Clamp(lp2 * 6.5f, -.6f, .6f) * env;
        }
        int fade = rate / 2;
        for (int i = 0; i < fade; i++)
        {
            float k = i / (float)fade;
            data[i] *= k;
            data[len - 1 - i] *= k;
        }
        var clip = AudioClip.Create("room_tone", len, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // ---------------- 开关 ----------------

    /// <summary>自检用：直接打开 / 关闭。</summary>
    public void OpenForTest()
    {
        if (!open) Open();
    }

    public void CloseForTest()
    {
        if (open) Close();
    }

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape)) return;

        if (open) { Close(); return; }
        if (!CanOpen()) return;
        Open();
    }

    bool CanOpen()
    {
        if (UiInputBlocker.Active) return false;                 // 密码键盘这类独占界面优先
        var l2 = Level2Ui.Instance;
        if (l2 && l2.IsModal) return false;                      // 问答/信/结局正在播
        var story = StoryProgress.Instance;
        if (story && story.IsReading) return false;
        var inv = FindObjectOfType<PlayerInventory>();
        if (inv && inv.IsOpen) return false;                     // 先让 Esc 关背包
        return true;
    }

    void Open()
    {
        open = true;
        ApplySensitivity();
        UiInputBlocker.Active = true;
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void Close()
    {
        open = false;
        UiInputBlocker.Active = false;
        Time.timeScale = 1f;
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Toast(string message)
    {
        toast = message;
        toastUntil = Time.unscaledTime + 3.2f;
    }

    // ---------------- 绘制 ----------------

    GUIStyle Style(int size, TextAnchor anchor, Color color)
        => new GUIStyle(GUI.skin.label)
        { font = font, fontSize = size, alignment = anchor, wordWrap = true, normal = { textColor = color } };

    void Fill(Rect r, Color c)
    {
        var old = GUI.color; GUI.color = c; GUI.DrawTexture(r, white); GUI.color = old;
    }

    void Outline(Rect r, Color c, float t = 2f)
    {
        Fill(new Rect(r.x, r.y, r.width, t), c);
        Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
        Fill(new Rect(r.x, r.y, t, r.height), c);
        Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
    }

    void OnGUI()
    {
        if (!open) return;
        GUI.depth = -1000;

        Fill(new Rect(0, 0, Screen.width, Screen.height), Ink);

        // 固定 980×640 的设计画布，按屏幕等比缩放 —— 不管 Game 视图是 16:9 还是别的比例，
        // 面板都不会被裁掉、控件也不会挤到一起。
        const float BW = 980f, BH = 640f;
        float s = Mathf.Min(Screen.width / BW, Screen.height / BH) * .97f;
        var oldMatrix = GUI.matrix;
        GUI.matrix = Matrix4x4.TRS(
            new Vector3((Screen.width - BW * s) * .5f, (Screen.height - BH * s) * .5f, 0f),
            Quaternion.identity, new Vector3(s, s, 1f));

        var panel = new Rect((BW - 900f) * .5f, (BH - 580f) * .5f, 900f, 580f);
        Fill(panel, PanelFill);
        Outline(panel, PanelEdge);

        // 顶部一条冷光，呼应第二关那种“旧屋窗外的天光”
        Fill(new Rect(panel.x, panel.y, panel.width, 3f), new Color(.55f, .82f, 1f, .75f));

        float pad = 44f;
        float x = panel.x + pad, inner = panel.width - pad * 2f;
        float y = panel.y + 30f;

        GUI.Label(new Rect(x, y, inner, 52f), "设  置", Style(36, TextAnchor.MiddleLeft, Accent));
        GUI.Label(new Rect(x, y + 6f, inner, 40f),
            open ? "Esc 返回游戏" : "", Style(18, TextAnchor.MiddleRight, TextDim));
        y += 62f;
        Fill(new Rect(x, y, inner, 1f), Line);
        y += 30f;

        // ---- 音量 ----
        y = DrawSlider(x, y, inner, "背景音量", ref volume, 0f, 1f,
            Mathf.RoundToInt(volume * 100f) + "%", v =>
            {
                volume = v;
                AudioListener.volume = volume;
                PlayerPrefs.SetFloat(VolKey, volume);
                PlayerPrefs.Save();
            });

        // ---- 灵敏度 ----
        y = DrawSlider(x, y + 10f, inner, "视角灵敏度", ref sensitivity, .3f, 4.5f,
            sensitivity.ToString("F1"), v =>
            {
                sensitivity = v;
                ApplySensitivity();
                PlayerPrefs.SetFloat(SensKey, sensitivity);
                PlayerPrefs.Save();
            });

        y += 30f;
        Fill(new Rect(x, y, inner, 1f), Line);
        y += 34f;

        // ---- 保存 ----
        if (Button(new Rect(x, y, inner, 54f), "保 存 进 度", true))
        {
            int slot = SaveSystem.SaveToNextSlot(out var saved);
            Toast("进度已保存 · " + saved.stamp);
        }
        y += 70f;

        // ---- 退出 ----
        float half = (inner - 14f) * .5f;
        if (Button(new Rect(x, y, half, 50f), "保存并退出游戏", true))
        {
            SaveSystem.SaveToNextSlot(out _);
            Toast("进度已保存，正在退出…");
            StartCoroutine(QuitSoon());
        }
        if (Button(new Rect(x + half + 14f, y, half, 50f), "继续游戏（Esc）", false))
            Close();

        // ---- 底部说明 ----
        GUI.Label(new Rect(x, panel.yMax - 46f, inner, 30f),
            "进度保存在本机。",
            Style(15, TextAnchor.MiddleCenter, new Color(.48f, .58f, .68f)));

        if (Time.unscaledTime < toastUntil && !string.IsNullOrEmpty(toast))
        {
            float tw = Mathf.Min(inner, 620f);
            var tr = new Rect(panel.x + (panel.width - tw) * .5f, panel.yMax - 94f, tw, 36f);
            Fill(tr, new Color(.04f, .10f, .16f, .96f));
            Outline(tr, new Color(.36f, .64f, .90f, .60f), 1f);
            GUI.Label(tr, toast, Style(17, TextAnchor.MiddleCenter, Accent));
        }

        GUI.matrix = oldMatrix;
    }

    float DrawSlider(float x, float y, float width, string label, ref float value, float min, float max,
        string readout, Action<float> onChanged)
    {
        GUI.Label(new Rect(x, y, 220f, 30f), label, Style(21, TextAnchor.MiddleLeft, TextMain));
        GUI.Label(new Rect(x + width - 90f, y, 90f, 30f), readout, Style(19, TextAnchor.MiddleRight, Accent));
        y += 34f;

        var track = new Rect(x, y + 9f, width, 6f);
        Fill(track, new Color(.12f, .20f, .28f, .95f));

        float t = Mathf.InverseLerp(min, max, value);
        Fill(new Rect(track.x, track.y, track.width * t, track.height), AccentSoft);

        var knob = new Rect(track.x + track.width * t - 7f, track.y - 6f, 14f, 18f);
        Fill(knob, new Color(.72f, .92f, 1f, 1f));
        Outline(knob, new Color(.36f, .66f, .90f, .9f), 1f);

        // 轨道上下各留 14px 的判定区，点起来不费劲
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

    bool Button(Rect r, string label, bool primary)
    {
        bool hover = r.Contains(Event.current.mousePosition);
        Fill(r, hover ? BtnHover : BtnFill);
        Outline(r, hover ? Accent : (primary ? new Color(.34f, .60f, .84f, .75f) : new Color(.26f, .38f, .50f, .60f)));
        if (primary && !hover)
            Fill(new Rect(r.x, r.y, 3f, r.height), AccentSoft);

        var style = Style(21, TextAnchor.MiddleCenter, hover ? Color.white : TextMain);
        return GUI.Button(r, label, style);
    }

    System.Collections.IEnumerator QuitSoon()
    {
        yield return new WaitForSecondsRealtime(.35f);
        QuitNow();
    }

    void QuitNow()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
