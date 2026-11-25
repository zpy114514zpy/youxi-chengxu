using UnityEngine;

/// <summary>
/// 运行时建材质用的着色器取用口。
///
/// 坑：编辑器里 Shader.Find("Universal Render Pipeline/Unlit") 一定有，但打包后
/// 只要场景里没有任何材质引用它，Unity 就会把这支 shader 整个裁掉，Shader.Find 返回 null，
/// 于是 new Material(null) 直接抛 ArgumentNullException —— 描边、光点、纸片全没了。
/// 现在三支常用 shader 都写进了 ProjectSettings/GraphicsSettings.asset 的常驻列表，
/// 这里再兜一层缓存和告警，避免每帧 Find，也方便出问题时一眼看出缺哪支。
/// </summary>
public static class RuntimeShaders
{
    static Shader unlit, lit;

    /// <summary>无光照：光点、色块、描边底座这类“不要被灯影响”的东西。</summary>
    public static Shader Unlit
    {
        get
        {
            if (!unlit) unlit = Pick("Universal Render Pipeline/Unlit", "Unlit/Color", "Sprites/Default");
            return unlit;
        }
    }

    /// <summary>受光：运行时拼出来的木座、纸片。</summary>
    public static Shader Lit
    {
        get
        {
            if (!lit) lit = Pick("Universal Render Pipeline/Lit", "Standard");
            return lit;
        }
    }

    static Shader Pick(params string[] names)
    {
        foreach (var name in names)
        {
            var shader = Shader.Find(name);
            if (shader) return shader;
        }
        Debug.LogWarning("[材质] 打包后找不到着色器：" + string.Join(" / ", names) +
                         "（要么场景里没人引用它被裁了，要么 Graphics 常驻列表里漏了）");
        return null;
    }
}
