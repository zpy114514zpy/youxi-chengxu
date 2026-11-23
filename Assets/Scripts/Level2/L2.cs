using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 第二关所有 E 交互的基类。挂在带 Collider 的物体上，由 PlayerInventory 统一分发，
/// 这样不用给 PlayerInventory 加一堆 if，也不会碰到第一关的任何逻辑。
/// </summary>
public abstract class L2Interactable : MonoBehaviour
{
    public abstract string Prompt { get; }
    public abstract void Interact(PlayerInventory inventory);
}

/// <summary>
/// 第二关共用的小工具：按名字找物体、量世界包围盒、给一件道具包一个“拾取宿主”、
/// 以及统一的一层淡蓝描边（只勾轮廓，不发光 —— 这一关的道具一律不加任何灯）。
/// </summary>
public static class L2
{
    // 道具高亮：淡蓝描边。宽度收得很细，够看清形状又不糊成一圈光。
    public static readonly Color OutlineBlue = new Color(.36f, .70f, 1f, 1f);
    public const float OutlineWidth = .0075f;
    public const float OutlineReveal = 3.4f;

    public static Transform Find(string name)
    {
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
            if (t.name == name && t.gameObject.scene.IsValid()) return t;
        return null;
    }

    public static List<Transform> FindAll(string name)
    {
        var list = new List<Transform>();
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
            if (t.name == name && t.gameObject.scene.IsValid()) list.Add(t);
        return list;
    }

    /// <summary>名字里包含某段文字的所有物体（用于 wallpaper / 变体命名）。</summary>
    public static List<Transform> FindContains(string fragment)
    {
        var list = new List<Transform>();
        foreach (var t in Object.FindObjectsOfType<Transform>(true))
            if (t.gameObject.scene.IsValid() && t.name.IndexOf(fragment, System.StringComparison.OrdinalIgnoreCase) >= 0)
                list.Add(t);
        return list;
    }

    public static Bounds BoundsOf(Transform t)
    {
        var rs = t.GetComponentsInChildren<Renderer>(true);
        bool has = false;
        var b = new Bounds(t.position, Vector3.zero);
        foreach (var r in rs)
        {
            if (!r) continue;
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    public static Bounds BoundsOf(IEnumerable<Renderer> rs)
    {
        bool has = false;
        var b = new Bounds(Vector3.zero, Vector3.zero);
        foreach (var r in rs)
        {
            if (!r) continue;
            if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    /// <summary>
    /// 把一件场景里的装饰物变成可拾取道具：
    /// 新建一个世界缩放为 1 的空壳当“拾取宿主”（原物体整个塞进去），
    /// 碰撞盒挂在宿主上。这样不管原物体的父级缩放是 0.23 还是 3484，碰撞盒尺寸都对得上。
    /// </summary>
    public static Transform WrapPickup(Transform visual, string displayName, string storyId,
        string itemType = "道具", float minXZ = .12f, float minY = .06f, string description = "")
    {
        if (!visual) return null;
        var bounds = BoundsOf(visual);

        var host = new GameObject("道具_" + displayName);
        host.transform.position = bounds.center;
        host.transform.rotation = Quaternion.identity;
        visual.SetParent(host.transform, true);

        var box = host.AddComponent<BoxCollider>();
        box.size = new Vector3(Mathf.Max(bounds.size.x, minXZ), Mathf.Max(bounds.size.y, minY),
                               Mathf.Max(bounds.size.z, minXZ));
        box.center = Vector3.zero;

        var pick = host.AddComponent<PickupItem>();
        pick.displayName = displayName;
        pick.storyId = storyId;
        pick.itemType = itemType;
        pick.description = description;

        AddBlueOutline(host.transform);
        return host.transform;
    }

    public static ProximityOutline AddBlueOutline(Transform t, Color? color = null, float width = OutlineWidth, float reveal = OutlineReveal)
    {
        if (!t) return null;
        var outline = t.GetComponent<ProximityOutline>();
        if (!outline) outline = t.gameObject.AddComponent<ProximityOutline>();
        outline.Apply(color ?? OutlineBlue, width, reveal);
        return outline;
    }

    /// <summary>在一个已有物体上加一个“看不见的交互盒”（用于门、相框、家具表面这类不该变形的目标）。</summary>
    public static BoxCollider AddBox(Transform t, Vector3 worldSize, Vector3 worldCenterOffset)
    {
        var box = t.gameObject.GetComponent<BoxCollider>();
        if (!box) box = t.gameObject.AddComponent<BoxCollider>();
        var s = t.lossyScale;
        box.size = new Vector3(worldSize.x / Mathf.Max(.0001f, Mathf.Abs(s.x)),
                               worldSize.y / Mathf.Max(.0001f, Mathf.Abs(s.y)),
                               worldSize.z / Mathf.Max(.0001f, Mathf.Abs(s.z)));
        box.center = t.InverseTransformVector(worldCenterOffset);
        return box;
    }

    public static Material Unlit(Color color, bool transparent = false)
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (!shader) shader = Shader.Find("Unlit/Color");
        if (!shader) shader = Shader.Find("Sprites/Default");
        var m = new Material(shader) { hideFlags = HideFlags.DontSave };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        if (transparent)
        {
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            if (m.HasProperty("_Blend")) m.SetFloat("_Blend", 0f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
        return m;
    }

    /// <summary>造一个立方体（不带碰撞），返回它的 Transform。</summary>
    public static Transform Box(string name, Transform parent, Vector3 worldCenter, Vector3 worldSize,
        Material material, Quaternion rotation)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.Destroy(go.GetComponent<Collider>());
        var t = go.transform;
        t.SetParent(parent, true);
        t.SetPositionAndRotation(worldCenter, rotation);
        t.localScale = worldSize;              // 父级缩放为 1 时，localScale 就是世界尺寸
        var r = go.GetComponent<Renderer>();
        if (material) r.sharedMaterial = material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        return t;
    }

    /// <summary>程序生成一张纯色贴图（UI 底板用）。</summary>
    public static Texture2D SolidTexture(Color color)
    {
        var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
        var px = new Color[16];
        for (int i = 0; i < px.Length; i++) px[i] = color;
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// 程序生成的“成人剪影”：镜面里长高长大的那个身影。
    /// 用圆角矩形拼一个人形，比直接贴一张 T-pose 三视图自然得多。
    /// </summary>
    public static Texture2D AdultSilhouette(int w = 256, int h = 512)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
        var px = new Color[w * h];
        var clear = new Color(0f, 0f, 0f, 0f);
        var ink = new Color(.05f, .07f, .10f, .88f);
        for (int i = 0; i < px.Length; i++) px[i] = clear;

        void Fill(float cx, float cy, float rx, float ry)
        {
            int x0 = Mathf.FloorToInt((cx - rx) * w), x1 = Mathf.CeilToInt((cx + rx) * w);
            int y0 = Mathf.FloorToInt((cy - ry) * h), y1 = Mathf.CeilToInt((cy + ry) * h);
            for (int y = Mathf.Max(0, y0); y < Mathf.Min(h, y1); y++)
                for (int x = Mathf.Max(0, x0); x < Mathf.Min(w, x1); x++)
                {
                    float dx = (x + .5f) / w - cx, dy = (y + .5f) / h - cy;
                    if (dx * dx / (rx * rx) + dy * dy / (ry * ry) <= 1f) px[y * w + x] = ink;
                }
        }

        Fill(.5f, .90f, .085f, .070f);     // 头
        Fill(.5f, .795f, .055f, .035f);    // 颈
        Fill(.5f, .625f, .150f, .135f);    // 肩胸
        Fill(.5f, .455f, .110f, .090f);    // 腰
        Fill(.5f, .300f, .130f, .090f);    // 胯
        Fill(.425f, .170f, .052f, .135f);// 左腿
        Fill(.575f, .170f, .052f, .135f);// 右腿
        Fill(.330f, .520f, .040f, .150f);// 左臂
        Fill(.670f, .520f, .040f, .150f);// 右臂

        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    public static string Fmt(Vector3 v) => $"({v.x:F2}, {v.y:F2}, {v.z:F2})";
}
