using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>二楼“获取钥匙的地点”：把所有信封碎片放到这里之后进入第二关。</summary>
public sealed class FragmentDeposit : MonoBehaviour
{
    public string nextScene = "Level 2";
    public string requiredFragment = "信封碎片⑤";
    public bool filled;

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

        story?.EnqueueCenter("任务完成", "恭喜完成关卡1所有任务！");
        StartCoroutine(GoNextLevel());
    }

    IEnumerator GoNextLevel()
    {
        yield return new WaitForSecondsRealtime(3.2f);
        if (!Application.CanStreamedLevelBeLoaded(nextScene))
        {
            Debug.LogWarning("[碎片放置] 场景 " + nextScene + " 不在 Build Settings 里，无法加载。", this);
            StoryProgress.Instance?.Enqueue("提示", "下一关还没加进 Build Settings。");
            yield break;
        }
        SceneManager.LoadScene(nextScene);
    }
}
