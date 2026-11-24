using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 第一关「还没开灯」时的环境亮度（运行时装配，不改场景的 RenderSettings）。
///
/// 开局房间只有天空盒环境光 + 一盏方向光撑着，原来这两项都偏亮，
/// 所以“天还没开灯”看起来和白天一样。这里把它们压低：
///   · 环境光 0.45 → 约 0.13（DimScale）
///   · 方向光 1.0  → 约 0.24（SunDimScale）
/// 玩家用墙上的开关把房间灯打开后，自动平滑恢复到场景原始亮度；关灯再压暗。
/// 只影响 Level 1，第二关不动。
/// </summary>
public sealed class AmbientMood : MonoBehaviour
{
    // 压暗倍率：0.28 ≈ “天黑了、还没开灯”的能见度（很暗，但家具轮廓和地面还看得见）。
    // 想更亮调到 0.40，想更暗调到 0.18。再往下就真的看不清路了。
    const float DimScale = .28f;
    // 方向光稍微再压一点，避免地板/白墙还是亮的。
    const float SunDimScale = .24f;

    readonly List<RoomLamp> lamps = new List<RoomLamp>();
    readonly List<Light> suns = new List<Light>();
    readonly List<float> sunBase = new List<float>();

    float ambientBase;
    float blend;          // 0 = 压暗（未开灯），1 = 场景原始亮度（已开灯）
    float target;
    float nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.name != "Level 1") return;   // 只调第一关
        new GameObject("环境光调节").AddComponent<AmbientMood>();
    }

    void Start()
    {
        ambientBase = RenderSettings.ambientIntensity;

        // 只认真正在发光的平行光（场景里有两盏 Directional，一盏是关掉的）。
        foreach (var light in FindObjectsOfType<Light>())
        {
            if (!light || light.type != LightType.Directional) continue;
            if (!light.enabled || light.intensity <= .01f) continue;
            suns.Add(light);
            sunBase.Add(light.intensity);
        }
        lamps.AddRange(FindObjectsOfType<RoomLamp>());

        // 开局直接就是压暗状态：别让玩家先看到“亮一下再变暗”。
        blend = 0f;
        target = 0f;
        Apply();

        Debug.Log("[环境光] 开局压暗：环境光 " + ambientBase.ToString("F2") + " → "
            + (ambientBase * DimScale).ToString("F2") + "，平行光 " + suns.Count + " 盏已同步调低。");
    }

    void Update()
    {
        // 半秒扫一次房间灯的状态就够了；每帧遍历灯组太浪费。
        if (Time.unscaledTime >= nextScan)
        {
            nextScan = Time.unscaledTime + .4f;
            bool anyLampOn = false;
            for (int i = 0; i < lamps.Count; i++)
                if (lamps[i] && lamps[i].isOn) { anyLampOn = true; break; }
            target = anyLampOn ? 1f : 0f;
        }

        if (Mathf.Abs(blend - target) < .001f) return;
        // 约 1 秒过渡：开灯/关灯时不会“啪”地一下跳变。
        blend = Mathf.MoveTowards(blend, target, Time.unscaledDeltaTime * 1.1f);
        Apply();
    }

    void Apply()
    {
        RenderSettings.ambientIntensity = ambientBase * Mathf.Lerp(DimScale, 1f, blend);
        for (int i = 0; i < suns.Count; i++)
            if (suns[i]) suns[i].intensity = sunBase[i] * Mathf.Lerp(SunDimScale, 1f, blend);
    }
}
