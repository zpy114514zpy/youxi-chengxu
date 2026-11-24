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

    /// <summary>背景音乐音量（0~1）。设置界面里的“背景音量”会乘在 AudioListener 上。</summary>
    public const float BgmVolume = .55f;

    /// <summary>走多远算一步（米）。走速 2 m/s 时约 2.2 步/秒，接近正常步频。</summary>
    public const float StepLength = .90f;

    AudioSource bgm, sfx, steps, push;
    AudioClip bgmClip, pickupClip, stepClip, pushClip;
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

        bgmClip = Resources.Load<AudioClip>("GameAudio/bgm_old_house");
        pickupClip = Resources.Load<AudioClip>("GameAudio/pickup_short");
        stepClip = Resources.Load<AudioClip>("GameAudio/step_single");
        pushClip = Resources.Load<AudioClip>("GameAudio/push_loop");

        if (!bgmClip) Debug.LogWarning("[音频] 找不到背景音乐 Resources/GameAudio/bgm_old_house");
        if (!pickupClip) Debug.LogWarning("[音频] 找不到拾取音效 Resources/GameAudio/pickup_short");
        if (!stepClip) Debug.LogWarning("[音频] 找不到脚步音效 Resources/GameAudio/step_single");
        if (!pushClip) Debug.LogWarning("[音频] 找不到推动音效 Resources/GameAudio/push_loop");

        bgm = Make("背景音乐", bgmClip, BgmVolume, true);
        // 拾取 / 放置：clips 由 PlayOneShot 传，音源自己不挂 clip。
        sfx = Make("拾取放置音效", null, 1f, false);
        // 脚步：每一步 PlayOneShot，音高随机一点，别听出机械重复。
        steps = Make("脚步声", null, .85f, false);
        push = Make("推动音效", pushClip, .62f, true);

        SceneManager.sceneLoaded += OnSceneLoaded;
        EnsureListener();

        if (bgmClip) { bgm.Play(); Debug.Log($"[音频] 背景音乐开始播放，时长 {bgmClip.length:F1}s"); }
        Debug.Log($"[音频] 音效载入：拾取 {(pickupClip ? pickupClip.length.ToString("F2") + "s" : "缺失")}，" +
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

    /// <summary>拾取道具。</summary>
    public static void PlayPickup() => PlaySfx(1f);

    /// <summary>放置道具（和拾取同一段音效，音高抬一点，听感上是“放下去”）。</summary>
    public static void PlayPlace() => PlaySfx(1.06f);

    static void PlaySfx(float pitch)
    {
        var a = Instance;
        if (!a)
        {
            Debug.LogWarning("[音频] GameAudio 还没初始化，拾取/放置音效被跳过");
            return;
        }
        if (!a.sfx || !a.pickupClip) return;
        a.sfx.pitch = pitch;
        a.sfx.PlayOneShot(a.pickupClip, 1f);
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
