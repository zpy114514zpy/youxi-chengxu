using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩偶归位点的“蓝色提示”：一根自下而上淡出的蓝光柱 + 地面上的一圈光环。
///
/// 只在玩家已经捡齐四个玩偶、并且真的进了二楼一号房之后才亮；
/// 对应的摆件放好之后，那一处自己就灭掉（不能拿光柱骗玩家）。
/// 材质全走加法混合，在昏暗的房间里发亮但不刺眼，也不会盖住柜面/凳面本身的颜色。
/// </summary>
public sealed class Level2SpotHint : MonoBehaviour
{
    static readonly Color HintColor = new Color(.30f, .66f, 1f, 1f);

    Transform column, ring;
    Light lamp;
    Material columnMat, ringMat;
    float seed;
    float radius = .17f, height = .62f;
    float baseAlpha = .55f;
    bool visible;

    public bool Visible => visible;

    public void Build(float size = .17f, float tall = .62f)
    {
        radius = size;
        height = tall;
        seed = Random.Range(0f, 10f);

        columnMat = MakeGlowMaterial(HintColor, BeamTexture());
        ringMat = MakeGlowMaterial(HintColor, RingTexture());
        column = MakeBeam("提示光柱", transform, radius, height, columnMat);
        ring = L2.Quad("提示光环", null, transform.position + Vector3.up * .006f,
            new Vector2(radius * 3.1f, radius * 3.1f), ringMat, Quaternion.Euler(-90f, 0f, 0f));

        var go = new GameObject("提示灯");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * .22f;
        lamp = go.AddComponent<Light>();
        lamp.type = LightType.Point;
        lamp.color = new Color(.34f, .64f, 1f);
        lamp.range = 1.15f;
        lamp.intensity = .30f;
        lamp.shadows = LightShadows.None;

        SetVisible(false);
    }

    public void SetVisible(bool on)
    {
        visible = on;
        if (column) column.gameObject.SetActive(on);
        if (ring) ring.gameObject.SetActive(on);
        if (lamp) lamp.enabled = on;
    }

    void Update()
    {
        if (!visible) return;
        float t = Time.time * 1.7f + seed;
        float pulse = .72f + .28f * Mathf.Sin(t);
        if (columnMat)
        {
            var c = HintColor;
            c.a = baseAlpha * pulse;
            ApplyColor(columnMat, c);
        }
        if (ringMat)
        {
            var c = HintColor;
            c.a = (baseAlpha + .16f) * (.75f + .25f * Mathf.Sin(t + .9f));
            ApplyColor(ringMat, c);
            float s = .92f + .10f * Mathf.Sin(t + .9f);
            if (ring) ring.localScale = new Vector3(radius * 3.1f * s, radius * 3.1f * s, 1f);
        }
        if (lamp) lamp.intensity = .22f + .12f * pulse;
    }

    static void ApplyColor(Material m, Color c)
    {
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Color")) m.SetColor("_Color", c);
    }

    static Material MakeGlowMaterial(Color color, Texture2D tex)
    {
        var m = L2.Unlit(Color.white, true);
        if (tex) m.SetTexture("_BaseMap", tex);
        // 加法混合：房间暗，光柱叠在家具上只会“更亮”，不会糊成一块蓝板。
        if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = 3050;
        if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
        ApplyColor(m, color);
        return m;
    }

    static Transform MakeBeam(string name, Transform parent, float radius, float height, Material mat)
    {
        const int Seg = 28;
        var go = new GameObject(name);
        var mf = go.AddComponent<MeshFilter>();
        var mr = go.AddComponent<MeshRenderer>();
        var mesh = new Mesh { name = "hint_beam" };

        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();
        for (int i = 0; i <= Seg; i++)
        {
            float a = i / (float)Seg * Mathf.PI * 2f;
            float x = Mathf.Cos(a) * radius, z = Mathf.Sin(a) * radius;
            verts.Add(new Vector3(x, 0f, z));
            verts.Add(new Vector3(x, height, z));
            uvs.Add(new Vector2(i / (float)Seg, 0f));
            uvs.Add(new Vector2(i / (float)Seg, 1f));
        }
        for (int i = 0; i < Seg; i++)
        {
            int b = i * 2;
            tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
            tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
        }
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mf.sharedMesh = mesh;

        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = Vector3.zero;
        return go.transform;
    }

    /// <summary>上淡下浓、竖着的一条柔光（横截面是圆的，绕一圈都看得见）。</summary>
    static Texture2D BeamTexture(int h = 128)
    {
        var tex = new Texture2D(4, h, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
        var px = new Color[4 * h];
        for (int y = 0; y < h; y++)
        {
            float v = y / (float)(h - 1);
            float a = Mathf.Pow(1f - v, 1.6f) * .82f;          // 底部最亮，往上淡出
            a *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(v / .08f)); // 贴地那一圈别硬切
            for (int x = 0; x < 4; x++) px[y * 4 + x] = new Color(1f, 1f, 1f, a);
        }
        tex.SetPixels(px);
        tex.wrapMode = TextureWrapMode.Repeat;
        tex.Apply();
        return tex;
    }

    /// <summary>地面上一圈柔光环：中间空、边缘软。</summary>
    static Texture2D RingTexture(int size = 128)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + .5f) / size * 2f - 1f;
                float dy = (y + .5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                // 环带中心在 r≈.78，内外各淡出
                float ring = Mathf.Clamp01(1f - Mathf.Abs(r - .78f) / .22f);
                ring = Mathf.Pow(ring, 1.6f);
                float inner = Mathf.Clamp01((r - .12f) / .30f);     // 中间留空，别糊成一张圆片
                float a = ring * inner * .85f;
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        tex.SetPixels(px);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.Apply();
        return tex;
    }
}
