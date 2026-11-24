using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 全屏转场：淡到一片暖白 → 加载下一关 → 再从白里淡回来。
/// 遮罩挂在 DontDestroyOnLoad 上，所以过场的那一帧不会闪出黑屏或旧场景。
/// </summary>
[DisallowMultipleComponent]
public sealed class ScreenFader : MonoBehaviour
{
    static ScreenFader instance;
    CanvasGroup group;
    Image cover;
    bool busy;

    public static bool Busy => instance && instance.busy;

    /// <summary>淡出 → 切场景 → 淡入。重复调用会被忽略（正在转场时）。</summary>
    public static void ToScene(string scene, Color color, float fadeOut = 1.1f, float fadeIn = 1.3f)
    {
        var f = Get();
        if (f.busy) return;
        f.StartCoroutine(f.Run(scene, color, fadeOut, fadeIn));
    }

    static ScreenFader Get()
    {
        if (instance) return instance;
        var go = new GameObject("转场遮罩");
        DontDestroyOnLoad(go);
        instance = go.AddComponent<ScreenFader>();
        instance.Build();
        return instance;
    }

    void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 32000;

        group = gameObject.AddComponent<CanvasGroup>();
        group.alpha = 1f;
        group.blocksRaycasts = false;
        group.interactable = false;

        var go = new GameObject("遮罩", typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        cover = go.AddComponent<Image>();
        cover.raycastTarget = false;
        cover.color = Color.clear;
    }

    IEnumerator Run(string scene, Color color, float fadeOut, float fadeIn)
    {
        busy = true;

        // 淡出（不受 timeScale 影响：剧情页把时间停住时也要能转场）
        float t = 0f;
        while (t < fadeOut)
        {
            t += Time.unscaledDeltaTime;
            cover.color = new Color(color.r, color.g, color.b, Mathf.Clamp01(t / fadeOut));
            yield return null;
        }
        cover.color = new Color(color.r, color.g, color.b, 1f);

        if (!Application.CanStreamedLevelBeLoaded(scene))
        {
            Debug.LogWarning("[转场] 场景 " + scene + " 不在 Build Settings 里，无法加载。");
            StoryProgress.Instance?.Enqueue("提示", "下一关还没加进 Build Settings。");
            float back = 0f;
            while (back < .6f)
            {
                back += Time.unscaledDeltaTime;
                cover.color = new Color(color.r, color.g, color.b, 1f - Mathf.Clamp01(back / .6f));
                yield return null;
            }
            cover.color = Color.clear;
            busy = false;
            yield break;
        }

        var op = SceneManager.LoadSceneAsync(scene);
        while (op != null && !op.isDone) yield return null;

        // 新场景刚醒来的那几帧会自己搭东西（灯、雾、UI），多盖 0.25 秒再露出来
        yield return new WaitForSecondsRealtime(.25f);

        t = 0f;
        while (t < fadeIn)
        {
            t += Time.unscaledDeltaTime;
            cover.color = new Color(color.r, color.g, color.b, 1f - Mathf.Clamp01(t / fadeIn));
            yield return null;
        }
        cover.color = Color.clear;
        group.alpha = 1f;
        busy = false;
    }
}
