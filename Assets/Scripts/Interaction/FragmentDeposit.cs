using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>二楼“获取钥匙的地点”：把所有信封碎片放到这里之后，开一道通往第二关的光门。</summary>
public sealed class FragmentDeposit : MonoBehaviour
{
    public string nextScene = "Level 2";
    public string requiredFragment = "信封碎片⑤";
    public bool filled;
    public LevelPortal portal;

    Light[] glows;
    float[] baseIntensity;

    public string Prompt => filled ? "碎片已经放好了" : "E 放置所有碎片";

    void Awake() { CacheGlows(); }

    /// <summary>把子级的黄灯缓存下来做呼吸。装配脚本建完灯之后要主动调一次。</summary>
    public void CacheGlows()
    {
        glows = GetComponentsInChildren<Light>(true);
        baseIntensity = new float[glows.Length];
        for (int i = 0; i < glows.Length; i++)
            baseIntensity[i] = glows[i] ? glows[i].intensity : 0f;
    }

    void Update()
    {
        if (filled || glows == null || glows.Length == 0) return;
        // 一明一暗地呼吸：二楼走廊离得远也能注意到这里有个东西
        float k = Mathf.Clamp01(.5f + .5f * Mathf.Sin(Time.time * 2.1f));
        for (int i = 0; i < glows.Length; i++)
            if (glows[i]) glows[i].intensity = baseIntensity[i] * (.55f + .65f * k);
    }

    public void Place()
    {
        if (filled) return;

        var story = StoryProgress.Instance;
        bool has = story == null || story.fragments.Contains(requiredFragment);
        if (!has)
        {
            story?.Enqueue("提示", "碎片还没集齐，先把这一关的信封碎片找出来。");
            return;
        }

        filled = true;
        GameAudio.PlayPlace();
        foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false;
        foreach (var c in GetComponentsInChildren<Collider>()) c.enabled = false;
        foreach (var l in GetComponentsInChildren<Light>()) l.enabled = false;

        story?.EnqueueCenter("任务完成", "恭喜完成关卡1所有任务！\n走进那道光门，去往下一章。");
        StartCoroutine(OpenPortal());
    }

    /// <summary>
    /// 碎片归位的瞬间不直接切场景 —— 先在旁边立一道光门，让玩家自己走进去。
    /// 位置交给 LevelPortal 自己找：挑最空的方向、落到地面上、正面朝回放置点。
    /// </summary>
    IEnumerator OpenPortal()
    {
        yield return new WaitForSecondsRealtime(1.1f);
        portal = LevelPortal.OpenNear(transform.position, nextScene);

        if (!portal)
        {
            // 周围实在没地方立门（或被家具围死）：退回到直接加载，别把玩家卡在这一关
            Debug.LogWarning("[碎片放置] 光门没找到合适的落点，直接进入下一关。", this);
            StartCoroutine(GoNextLevel());
            yield break;
        }

        Debug.Log($"[碎片放置] 光门已开启：{portal.transform.position:F2}，朝向 {portal.transform.forward:F2}", this);
    }

    IEnumerator GoNextLevel()
    {
        yield return new WaitForSecondsRealtime(1.6f);
        if (!Application.CanStreamedLevelBeLoaded(nextScene))
        {
            Debug.LogWarning("[碎片放置] 场景 " + nextScene + " 不在 Build Settings 里，无法加载。", this);
            StoryProgress.Instance?.Enqueue("提示", "下一关还没加进 Build Settings。");
            yield break;
        }
        SceneManager.LoadScene(nextScene);
    }
}
