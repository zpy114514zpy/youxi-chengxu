using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 全局音频（第一关 / 第二关共用，切场景不中断、不重头播）：
///   · 背景音乐：一条循环 BGM，两关通用，过场景不重启；
///   · 拾取 / 放置：一次性音效；
///   · 推箱子 / 推凳子 / 推任何家具：推着的时候循环，松手就停；
///   · 走路：按实际走过的距离一步一步触发，走多快就响多快，停下就停。
/// 场景里原本没有 AudioListener（第二关就是一片静音），这里补一个，
/// 否则 AudioSource 播得再对也是听不见的。
/// </summary>
[DefaultExecutionOrder(-300)]
public sealed class GameAudio : MonoBehaviour
{
    public static GameAudio Instance { get; private set; }

    /// <summary>背景音乐音量（0~1）。设置界面里的“背景音量”会乘在 AudioListener 上。
    /// 这首和之前那首都是 -20 LUFS，音量沿用同一个值，换曲不用再动。</summary>
    public const float BgmVolume = .42f;

    /// <summary>走多远算一步（米）。走速 2 m/s 时约 2.2 步/秒，接近正常步频。</summary>
    public const float StepLength = .90f;

    AudioSource bgm, sfx, steps, push, page, world, pen;
    AudioClip bgmClip, itemClip, stepClip, pushClip, pageClip, doorClip, lampClip, portalClip, penClip;
    float lastPageAt = -1f;
    bool pushing;
    bool walkingState;
    float walkAccum;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Instance) return;
        var go = new GameObject("游戏音频");
        DontDestroyOnLoad(go);
        go.AddComponent<GameAudio>();
    }

    void Awake()
    {
        if (Instance && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // 背景音乐：玩家提供的整段曲子，已做成 213.6s 无缝循环，两关共用同一条。
        bgmClip = Resources.Load<AudioClip>("GameAudio/bgm_main");
        // 拾取 / 放置道具：玩家提供的「拾取道具音1」，两种操作用同一段。
        itemClip = Resources.Load<AudioClip>("GameAudio/pickup_item");
        stepClip = Resources.Load<AudioClip>("GameAudio/step_single");
        pushClip = Resources.Load<AudioClip>("GameAudio/push_loop");
        // 剧情提示翻页（按空格继续 / 点上一步）：玩家提供的「消息提示音」。
        pageClip = Resources.Load<AudioClip>("GameAudio/ui_page");
        // 开门 / 拉抽屉：真实录音（木门吱呀打开），开灯仍是合成音。
        doorClip = Resources.Load<AudioClip>("GameAudio/door_open");
        lampClip = Resources.Load<AudioClip>("GameAudio/lamp_switch");
        // 光门开启：一声 2.4 秒的“上扬 + 清铃”，转场时垫在淡入淡出下面。
        portalClip = Resources.Load<AudioClip>("GameAudio/portal_open");

        if (!bgmClip) Debug.LogWarning("[音频] 找不到背景音乐 Resources/GameAudio/bgm_main");
        if (!itemClip) Debug.LogWarning("[音频] 找不到拾取/放置音效 Resources/GameAudio/pickup_item");
        if (!stepClip) Debug.LogWarning("[音频] 找不到脚步音效 Resources/GameAudio/step_single");
        if (!pushClip) Debug.LogWarning("[音频] 找不到推动音效 Resources/GameAudio/push_loop");
        if (!pageClip) Debug.LogWarning("[音频] 找不到提示翻页音效 Resources/GameAudio/ui_page");
        if (!doorClip) Debug.LogWarning("[音频] 找不到开门音效 Resources/GameAudio/door_open");
        if (!lampClip) Debug.LogWarning("[音频] 找不到开关灯音效 Resources/GameAudio/lamp_switch");
        if (!portalClip) Debug.LogWarning("[音频] 找不到光门音效 Resources/GameAudio/portal_open");

        bgm = Make("背景音乐", bgmClip, BgmVolume, true);
        // 拾取 / 放置：clips 由 PlayOneShot 传，音源自己不挂 clip。
        sfx = Make("拾取放置音效", null, 1f, false);
        // 脚步声：每一步 PlayOneShot，音高随机一点，别听出机械重复。
        steps = Make("脚步声", null, .85f, false);
        push = Make("推动音效", pushClip, .62f, true);
        // 提示翻页：单独一条音源，免得和拾取/放置抢 pitch。
        page = Make("提示翻页音效", null, .9f, false);
        // 开门 / 开灯：再单独一条，和拾取、翻页都不互相干扰。
        world = Make("环境互动音效", null, 1f, false);
        // 信纸上的打字声：没有现成采样，运行时合成一记清脆的键帽声，一个字响一下。
        penClip = BuildKeyClick();
        pen = Make("打字音效", null, .9f, false);

        SceneManager.sceneLoaded += OnSceneLoaded;
        EnsureListener();

        if (bgmClip) { bgm.Play(); Debug.Log($"[音频] 背景音乐开始播放，时长 {bgmClip.length:F1}s"); }
        Debug.Log($"[音频] 音效载入：拾取/放置 {(itemClip ? itemClip.length.ToString("F2") + "s" : "缺失")}，" +
                  $"提示翻页 {(pageClip ? pageClip.length.ToString("F2") + "s" : "缺失")}，" +
                  $"开门 {(doorClip ? doorClip.length.ToString("F2") + "s" : "缺失")}，" +
                  $"开关灯 {(lampClip ? lampClip.length.ToString("F2") + "s" : "缺失")}，" +
                  $"光门 {(portalClip ? portalClip.length.ToString("F2") + "s" : "缺失")}，" +
                  $"脚步 {(stepClip ? stepClip.length.ToString("F2") + "s" : "缺失")}，" +
                  $"推 {(pushClip ? pushClip.length.ToString("F2") + "s" : "缺失")}");
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (Instance == this) Instance = null;
    }

    AudioSource Make(string name, AudioClip clip, float volume, bool loop)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, false);
        var src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.loop = loop;
        src.volume = volume;
        src.spatialBlend = 0f;          // 全 2D：第一人称，不需要空间衰减
        src.playOnAwake = false;
        src.ignoreListenerPause = true; // 剧情暂停 Time.timeScale=0 时，音效也别卡住
        return src;
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        EnsureListener();
        // 换场景后 AudioSource 挂在 DontDestroyOnLoad 上不会断，但保险起见补一次。
        if (bgmClip && bgm && !bgm.isPlaying) bgm.Play();
        pushing = false;
        walkAccum = 0f;
        walkingState = false;
        if (steps) steps.Stop();
        if (push) push.Stop();
    }

    /// <summary>场景里没有 AudioListener 时补一个（第二关原本一个都没有，等于全程静音）。</summary>
    void EnsureListener()
    {
        if (FindObjectOfType<AudioListener>()) return;
        var cam = Camera.main;
        if (cam) cam.gameObject.AddComponent<AudioListener>();
        else gameObject.AddComponent<AudioListener>();
        Debug.Log("[音频] 场景里没有 AudioListener，已补在 " + (cam ? cam.name : "游戏音频") + " 上");
    }

    // ---------------- 对外 ----------------

    /// <summary>拾取道具：玩家提供的「拾取道具音1」。</summary>
    public static void PlayPickup() => PlayItem(1f, "拾取");

    /// <summary>放置道具：和拾取同一段音效，音高抬 6%，听感上像“放下去”。</summary>
    public static void PlayPlace() => PlayItem(1.06f, "放置");

    static void PlayItem(float pitch, string what)
    {
        var a = Instance;
        if (!a)
        {
            Debug.LogWarning("[音频] GameAudio 还没初始化，" + what + "音效被跳过");
            return;
        }
        if (!a.sfx || !a.itemClip) return;
        a.sfx.pitch = pitch;
        // 玩家要求“小一点”：拾取/放置压到 0.62，垫在其他音效下面，别抢剧情提示音。
        a.sfx.PlayOneShot(a.itemClip, .62f);
    }

    /// <summary>
    /// 剧情提示翻页：玩家按空格继续、或点左下角“上一步”时响一下。
    /// 用玩家提供的「消息提示音」。0.12 秒内重复调用只响一声 ——
    /// “翻页那一下”和“新一页出现在屏幕上”几乎同一帧发生，不去重会连响两声。
    /// </summary>
    public static void PlayPage()
    {
        var a = Instance;
        if (!a || !a.page || !a.pageClip) return;
        if (Time.unscaledTime - a.lastPageAt < .12f) return;
        a.lastPageAt = Time.unscaledTime;
        a.page.Stop();                 // 连按空格 / 连点“上一步”时只留最新一声，别叠成一片
        a.page.pitch = 1f;
        // 玩家要求“小一点”：提示音压到 0.55，连点翻页也不刺耳。
        a.page.PlayOneShot(a.pageClip, .55f);
    }

    /// <summary>
    /// 信纸上的“打字”：每写出一个字响一下清脆的键盘声。
    /// 没有现成采样，就在运行时合成一小段键帽落下的“嗒”，音高 / 音量随机一点，
    /// 一秒近二十个字连着响，也不会听成机械的节拍。
    /// </summary>
    public static void PlayType()
    {
        var a = Instance;
        if (!a || !a.pen || !a.penClip) return;
        a.pen.pitch = Random.Range(.92f, 1.12f);
        a.pen.PlayOneShot(a.penClip, Random.Range(.38f, .62f));
    }

    /// <summary>
    /// 合成一记键盘敲击：极短的高频“嗒”（白噪声减低频后的嘶声）+ 两层键帽共鸣（2.4k / 1.1k），
    /// 衰减都很快，所以听起来是“清脆”而不是“闷”；0.06 秒就干净了，连着敲也不会糊成一片。
    /// </summary>
    static AudioClip BuildKeyClick()
    {
        const int sr = 44100;
        int n = Mathf.RoundToInt(sr * .06f);
        var data = new float[n];
        var rnd = new System.Random(20261125);
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = (float)i / sr;
            float tap = Mathf.Exp(-t * 260f);        // 键帽落下的那一下：4ms 内就收干
            float ring = Mathf.Exp(-t * 62f);        // 塑料键帽的余音
            float white = (float)(rnd.NextDouble() * 2.0 - 1.0);
            lp += (white - lp) * .16f;
            float hiss = white - lp;                 // 减掉低频 = 只剩清脆的高频
            float tone = Mathf.Sin(2f * Mathf.PI * 2450f * t) * .40f
                       + Mathf.Sin(2f * Mathf.PI * 1180f * t) * .20f;
            data[i] = Mathf.Clamp(hiss * tap * .60f + tone * ring * .45f, -1f, 1f);
        }
        var clip = AudioClip.Create("键盘敲击", n, 1, sr, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>
    /// 打开 / 关闭门、柜门、抽屉。
    /// pitch 1 = 正常的房门；柜门、抽屉更小更轻，调用处会抬一点音高。
    /// </summary>
    public static void PlayDoor(float pitch = 1f, float volume = .85f)
    {
        var a = Instance;
        if (!a || !a.world || !a.doorClip) return;
        a.world.pitch = Mathf.Clamp(pitch, .5f, 2f);
        a.world.PlayOneShot(a.doorClip, Mathf.Clamp01(volume));
    }

    /// <summary>光门开启 / 走进去：一声上扬的“嗡——叮”。</summary>
    public static void PlayPortal()
    {
        var a = Instance;
        if (!a || !a.world || !a.portalClip) return;
        a.world.pitch = 1f;
        a.world.PlayOneShot(a.portalClip, .9f);
    }

    /// <summary>
    /// 序幕视频播放期间把背景音乐压掉（否则视频里的配乐会和关卡 BGM 叠在一起）。
    /// 只动背景音乐这一条音源，拾取 / 脚步 / 视频自己的声音都不受影响。
    /// </summary>
    public static void SetBgmMuted(bool muted)
    {
        var a = Instance;
        if (!a || !a.bgm) return;
        a.bgm.mute = muted;
    }

    /// <summary>拨墙上的灯开关：一声短促的“咔哒”。</summary>
    public static void PlayLamp(float pitch = 1f, float volume = .9f)
    {
        var a = Instance;
        if (!a || !a.world || !a.lampClip) return;
        a.world.pitch = Mathf.Clamp(pitch, .5f, 2f);
        a.world.PlayOneShot(a.lampClip, Mathf.Clamp01(volume));
    }

    /// <summary>
    /// 走路推进的距离。每走满 <see cref="StepLength"/> 米就响一步。
    /// 不能直接 loop 那段 5 秒的脚步声：它里面只有 5 个脚步、固定 1 秒一步，
    /// 玩家走起来 2 米/秒，声音和步伐完全脱节，听着像“声音自己在走，人没走”。
    /// 按位移触发就是走一步响一下，一直走就一直有节奏地响，停下自然就停了。
    /// </summary>
    public static void AddWalkDistance(float meters)
    {
        var a = Instance;
        if (!a || !a.steps || !a.stepClip) return;
        if (meters <= .0001f) return;

        a.walkAccum += meters;
        if (a.walkAccum < StepLength) return;

        a.walkAccum -= StepLength;
        if (a.walkAccum > StepLength * 4f) a.walkAccum = 0f;   // 瞬移 / 大跳之后别连爆一串
        a.steps.pitch = Random.Range(.94f, 1.06f);
        a.steps.PlayOneShot(a.stepClip, .85f);
    }

    /// <summary>开始 / 停止走路。停下时把没走完的半步清零；起步先垫半步，第一声不用等太久。</summary>
    public static void SetWalking(bool on)
    {
        var a = Instance;
        if (!a) return;
        if (!on)
        {
            a.walkAccum = 0f;
            a.walkingState = false;
            return;
        }
        if (a.walkingState) return;
        a.walkingState = true;
        a.walkAccum = StepLength * .5f;      // 起步 0.45m（约 0.22s）就响第一声
    }

    /// <summary>推东西：开始推就循环响，松手 / 停下就停。</summary>
    public static void SetPushing(bool on)
    {
        var a = Instance;
        if (!a || !a.push || !a.pushClip || a.pushing == on) return;
        a.pushing = on;
        if (on) a.push.Play();
        else a.push.Stop();
    }
}
