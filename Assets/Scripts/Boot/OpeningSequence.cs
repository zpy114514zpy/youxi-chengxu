using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Video;

/// <summary>
/// 打开工程点播放之后的完整开场流程：
///   序幕视频（StreamingAssets/opening.mp4）→ 主菜单 → 点“开始游戏”→ 才真正进入关卡。
///
/// 实现方式：不新增场景、不改 Build Settings。
/// 第一关照常加载，加载完立刻把时间冻住（timeScale=0）、把玩家和第一关的剧情 UI 全部禁用，
/// 在上面盖一层视频；视频放完换成主菜单；点开始游戏再逐项恢复。
/// 这样场景文件一个字都不用动，读完档重进也不会二次播放。
/// </summary>
[DefaultExecutionOrder(-9000)]
public sealed class OpeningSequence : MonoBehaviour
{
    public static OpeningSequence Instance { get; private set; }

    enum Phase { Idle, Video, Menu, Playing }

    Phase phase = Phase.Idle;
    VideoPlayer video;
    RenderTexture rt;
    AudioSource videoAudio;
    MainMenuUi menu;
    Font font;
    float videoStart;
    float fadeAlpha = 0f;
    bool skipRequested;
    static bool playedOnce;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != "Level 1") return;
        if (playedOnce) return;              // 读档 / 重新加载场景不再播第二遍
        if (Instance) return;

        var go = new GameObject("开场流程");
        DontDestroyOnLoad(go);
        go.AddComponent<OpeningSequence>();
    }

    void Awake()
    {
        Instance = this;
        playedOnce = true;
        font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 24);
        StartCoroutine(Run());
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (font) Destroy(font);
        CleanupVideo();
    }

    IEnumerator Run()
    {
        // 场景刚加载：先冻住，再等一帧让所有 Start 跑完，然后再冻一次
        // （有些组件会在 Start 里自己锁鼠标 / 打开雾 / 弹第一页剧情）。
        FreezeGame();
        yield return null;
        FreezeGame();

        if (HasOpeningVideo())
            yield return PlayVideo();
        else
            Debug.LogWarning("[开场] 找不到 StreamingAssets/opening.mp4，直接进主菜单。");

        ShowMenu();
    }

    static string VideoPath => Path.Combine(Application.streamingAssetsPath, "opening.mp4");

    static bool HasOpeningVideo()
    {
        try { return File.Exists(VideoPath); }
        catch { return false; }
    }

    // ---------------------------------------------------------------- 冻结 / 恢复

    void FreezeGame()
    {
        Time.timeScale = 0f;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        UiInputBlocker.Active = true;
        SetGameplayEnabled(false);
    }

    /// <summary>
    /// 只关“会自己动、会自己弹东西”的几个组件，别的（灯、雾、场景装饰）一律不碰。
    /// StoryProgress 关掉之后，第一关的开场四页要等点了“开始游戏”才会出来。
    /// </summary>
    static void SetGameplayEnabled(bool on)
    {
        foreach (var c in FindObjectsOfType<PlayerController>(true)) if (c) c.enabled = on;
        foreach (var c in FindObjectsOfType<FirstPersonView>(true)) if (c) c.enabled = on;
        foreach (var c in FindObjectsOfType<PlayerInventory>(true)) if (c) c.enabled = on;
        foreach (var c in FindObjectsOfType<StoryProgress>(true)) if (c) c.enabled = on;
        foreach (var c in FindObjectsOfType<GameMenu>(true)) if (c) c.enabled = on;
    }

    // ---------------------------------------------------------------- 视频

    IEnumerator PlayVideo()
    {
        phase = Phase.Video;
        GameAudio.SetBgmMuted(true);

        rt = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32) { hideFlags = HideFlags.DontSave };
        rt.Create();

        var host = new GameObject("序幕视频");
        host.transform.SetParent(transform, false);

        videoAudio = host.AddComponent<AudioSource>();
        videoAudio.playOnAwake = false;
        videoAudio.volume = 1f;

        video = host.AddComponent<VideoPlayer>();
        video.playOnAwake = false;
        video.source = VideoSource.Url;
        video.url = VideoPath;
        video.renderMode = VideoRenderMode.RenderTexture;
        video.targetTexture = rt;
        video.audioOutputMode = VideoAudioOutputMode.AudioSource;
        video.controlledAudioTrackCount = 1;
        video.EnableAudioTrack(0, true);
        video.SetTargetAudioSource(0, videoAudio);
        video.isLooping = false;
        video.skipOnDrop = true;
        video.Prepare();

        // 解码准备最多等 8 秒；失败就直接进菜单，不能把玩家卡在黑屏上。
        float prep = 0f;
        while (video && !video.isPrepared && prep < 8f)
        {
            prep += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!video || !video.isPrepared)
        {
            Debug.LogWarning("[开场] 视频没能解码（" + VideoPath + "），直接进主菜单。");
            CleanupVideo();
            GameAudio.SetBgmMuted(false);
            yield break;
        }

        float duration = video.length > .1f ? (float)video.length : 20f;
        video.Play();
        videoStart = Time.unscaledTime;
        skipRequested = false;

        while (Time.unscaledTime - videoStart < duration + .8f)
        {
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) ||
                Input.GetMouseButtonDown(0))
            {
                skipRequested = true;
                break;
            }
            if (video && !video.isPlaying && Time.unscaledTime - videoStart > .6f) break;
            yield return null;
        }

        if (skipRequested) Debug.Log("[开场] 玩家跳过序幕视频");
        CleanupVideo();
        GameAudio.SetBgmMuted(false);
    }

    void CleanupVideo()
    {
        if (video)
        {
            video.Stop();
            if (video.gameObject) Destroy(video.gameObject);
            video = null;
        }
        if (rt)
        {
            rt.Release();
            Destroy(rt);
            rt = null;
        }
        videoAudio = null;
    }

    // ---------------------------------------------------------------- 主菜单

    void ShowMenu()
    {
        phase = Phase.Menu;

        var bg = Resources.Load<Texture2D>("StoryUI/main_menu_bg");
        if (!bg) Debug.LogWarning("[开场] 找不到 Resources/StoryUI/main_menu_bg，主菜单将只有文字。");

        menu = gameObject.AddComponent<MainMenuUi>();
        menu.Background = bg;
        menu.UiFont = font;
        menu.OnStartGame = BeginGame;
        menu.OnQuit = QuitGame;

        // 视频最后一帧直接切菜单太硬：盖一层黑，再淡出来。
        fadeAlpha = 1f;
        StartCoroutine(FadeTo(0f, .65f));
        Debug.Log("[开场] 序幕结束，显示主菜单");
    }

    System.Collections.IEnumerator FadeTo(float target, float duration)
    {
        float start = fadeAlpha, t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            fadeAlpha = Mathf.Lerp(start, target, Mathf.Clamp01(t / duration));
            yield return null;
        }
        fadeAlpha = target;
    }

    public void BeginGame()
    {
        if (phase != Phase.Menu) return;
        StartCoroutine(BeginRoutine());
    }

    IEnumerator BeginRoutine()
    {
        // 先黑下去，再把游戏放开，最后从黑里淡回来 —— 玩家不会从菜单“啪”地跳进关卡。
        yield return FadeTo(1f, .38f);
        phase = Phase.Playing;

        if (menu)
        {
            menu.enabled = false;
            Destroy(menu);
            menu = null;
        }

        Time.timeScale = 1f;
        UiInputBlocker.Active = false;
        SetGameplayEnabled(true);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        Debug.Log("[开场] 开始游戏");
        yield return FadeTo(0f, .75f);
    }

    public void QuitGame()
    {
        Debug.Log("[开场] 退出游戏");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---------------------------------------------------------------- 绘制

    void OnGUI()
    {
        GUI.depth = -4000;          // 盖在主菜单（-3000）和游戏内设置（-1000）之上

        if (phase == Phase.Video) DrawVideo();

        if (fadeAlpha > .001f)
        {
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, fadeAlpha);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = old;
        }
    }

    void DrawVideo()
    {
        var old = GUI.color;
        GUI.color = Color.black;
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = old;

        if (rt)
        {
            // 等比放，留黑边，绝不裁掉画面内容。
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            float w = 1280f * scale, h = 720f * scale;
            var rect = new Rect((Screen.width - w) * .5f, (Screen.height - h) * .5f, w, h);
            GUI.color = Color.white;
            GUI.DrawTexture(rect, rt, ScaleMode.StretchToFill, false);
            GUI.color = old;
        }

        if (!font) return;
        var style = new GUIStyle(GUI.skin.label)
        {
            font = font,
            fontSize = 16,
            alignment = TextAnchor.MiddleRight,
            normal = { textColor = new Color(1f, 1f, 1f, .45f) },
        };
        GUI.Label(new Rect(Screen.width - 360f, Screen.height - 54f, 320f, 30f), "空格 / 点击 跳过", style);
    }
}
