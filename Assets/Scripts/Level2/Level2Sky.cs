using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 第二关的夜色：给整栋房子换一张黄昏天空盒，并把环境光压暗到“能看清轮廓和灯，
/// 但看不清细节”的程度。只在 Level 2 调用，第一关一个字都不碰。
///
/// 两件事：
///   1. 天空盒 —— Resources/Level2Sky/sky_dusk（4096×2048 经纬图），
///      下半球是雾，所以从窗户往外看是“暗蓝的天 + 远处的白雾”，正好对上开场那句
///      “屋外的白雾，是尚未到来的未来”；
///   2. 环境光 —— 从场景原来的 (.106,.114,.130) 压到 (.048,.054,.068)，约四成半。
///
/// 故意**不加雾**：这栋房子一楼走廊有十几米，以相机为圆心的距离雾会把走廊糊成一堵
/// 白墙（之前已经踩过一次），所以暗只靠环境光和天空盒来给。
/// </summary>
public static class Level2Sky
{
    /// <summary>天空盒曝光。想让夜色更亮就往上调（1.2 左右接近“傍晚”），更暗调到 0.6。</summary>
    const float SkyExposure = .90f;

    public static void Apply()
    {
        ApplySkybox();
        ApplyAmbient();
        DynamicGI.UpdateEnvironment();
    }

    static void ApplySkybox()
    {
        var tex = Resources.Load<Texture2D>("Level2Sky/sky_dusk");
        // 优先用工程里的自定义 shader（从 Resources 读，打包后一定在）
        var shader = Resources.Load<Shader>("Shaders/SkyboxEquirect");
        if (!shader) shader = Shader.Find("House/SkyboxEquirect");

        if (!tex || !shader)
        {
            Debug.LogWarning($"[第二关] 天空盒没换成：贴图 {(tex ? "有" : "缺")}，shader {(shader ? shader.name : "缺")}。场景仍用原来的天空。");
            return;
        }

        var mat = new Material(shader) { hideFlags = HideFlags.DontSave };
        mat.SetTexture("_MainTex", tex);
        mat.SetFloat("_Exposure", SkyExposure);
        mat.SetColor("_Tint", new Color(.90f, .93f, 1f));
        RenderSettings.skybox = mat;

        Debug.Log($"[第二关] 天空盒已换上（{tex.width}×{tex.height}，曝光 {SkyExposure:F2}）");
    }

    static void ApplyAmbient()
    {
        // 用三色环境光而不是纯平色：天花板附近偏冷蓝、地面附近几乎全黑，
        // 物体上下才有明暗差，不然压暗之后整间屋子会糊成一块灰。
        //
        // 这三个颜色就是**最终亮度**（下面的 ambientIntensity 设成 1，不再相乘）。
        // 原来是场景的 (.212,.227,.259) × 0.5 ≈ (.106,.114,.130)，
        // 现在 (.048,.054,.068)，大约是原来的四成半。
        // 觉得太黑就把三组数各乘 1.4；觉得不够暗就乘 0.7。
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(.048f, .054f, .068f);
        RenderSettings.ambientEquatorColor = new Color(.026f, .030f, .038f);
        RenderSettings.ambientGroundColor = new Color(.011f, .012f, .015f);
        RenderSettings.ambientIntensity = 1f;

        // 金属、玻璃、镜框的高光来自天空盒的反射。不压的话，天一暗它们反而更扎眼。
        RenderSettings.reflectionIntensity = .25f;
    }
}
