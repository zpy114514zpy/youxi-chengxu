using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 关卡之间的光门：一圈会流动的金色光环绕着一面试卷般旋转的光幕，走进去就进入下一关。
///
/// 位置由 <see cref="OpenNear"/> 自动找：从放置点朝八个方向量“哪边最空”，
/// 把光门立在 1.5 米外的地板上，正面朝回放置点 —— 玩家刚放完碎片，一抬头就在正前方。
/// </summary>
[DisallowMultipleComponent]
public sealed class LevelPortal : MonoBehaviour
{
    public string nextScene = "Level 2";

    const float MajorRadius = 1.00f;     // 光环半径（门洞直径 2 米，刚好够角色走过去）
    const float Tube = .055f;
    const float RingCenterY = 1.06f;     // 环心离地高度

    Transform ring, disc, shards;
    Light glow;
    Material ringMat, discMat, sparkMat, floorMat;
    ParticleSystem sparks;
    float bornAt;
    bool entering;
    bool armed;                          // 门“长好”之后才允许走进去，免得一生成就把人吞掉
    float baseGlow = 2.2f;

    // ---------------------------------------------------------------- 生成

    /// <summary>
    /// 在 origin 周围挑一个真正立得住的方向，把光门放在地面上。
    ///
    /// 只看“朝前能走多远”是不够的：走廊正前方很空、两侧却都是墙的时候，
    /// 直径两米的环会有一半插进墙里。所以每个方向都按「朝前余量」和「左右余量」
    /// 两项一起算分，再扣掉落到下一层台阶的落差。
    /// </summary>
    public static LevelPortal OpenNear(Vector3 origin, string scene)
    {
        Vector3 bestDir = Vector3.zero;
        Vector3 bestFoot = origin;
        float bestScore = -9999f;

        foreach (var d in Dirs)
        {
            float fwd = Clearance(origin, d);
            if (fwd < .95f) continue;                       // 这个方向贴着墙或柜子，直接跳过

            // 退到离墙留一点余量的位置，但绝不能让环有一半立进墙里
            float dist = Mathf.Clamp(fwd - .55f, 1.05f, 1.85f);
            if (dist > fwd - .22f) dist = fwd - .22f;
            if (dist < .95f) continue;
            Vector3 foot = origin + d * dist;
            foot.y = GroundHeight(foot, origin.y);

            // 门立起来之后环会往左右各摊开一米，两侧都得有地方
            Vector3 side = Vector3.Cross(Vector3.up, -d).normalized;
            float lat = Mathf.Min(Clearance(foot, side), Clearance(foot, -side));

            // 落到下一层台阶 / 柜顶上就不像“一道门”了，按落差扣分
            float drop = Mathf.Abs(foot.y - origin.y);
            float score = Mathf.Min(fwd, lat * 1.25f) - drop * .8f;
            if (score > bestScore) { bestScore = score; bestDir = d; bestFoot = foot; }
        }

        // 四面都被堵死：别硬塞一道穿墙的门，交给调用方退回直接过场
        if (bestDir == Vector3.zero) return null;

        return Spawn(bestFoot, -bestDir, scene);
    }

    public static LevelPortal Spawn(Vector3 footPoint, Vector3 facing, string scene)
    {
        facing = Vector3.ProjectOnPlane(facing, Vector3.up);
        if (facing.sqrMagnitude < .001f) facing = Vector3.forward;

        var go = new GameObject("通往下一关的光门");
        go.transform.position = footPoint;
        go.transform.rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);

        var portal = go.AddComponent<LevelPortal>();
        portal.nextScene = scene;
        portal.Build();
        return portal;
    }

    static readonly Vector3[] Dirs =
    {
        Vector3.forward, Vector3.back, Vector3.left, Vector3.right,
        new Vector3(1, 0, 1).normalized, new Vector3(1, 0, -1).normalized,
        new Vector3(-1, 0, 1).normalized, new Vector3(-1, 0, -1).normalized
    };

    /// <summary>从某点往某个方向，三个高度各打一条 2.4 米的射线，取最近的阻挡距离。墙、家具都算。</summary>
    static float Clearance(Vector3 origin, Vector3 dir)
    {
        float gap = 2.4f;
        foreach (float h in new[] { .55f, 1.15f, 1.70f })
        {
            Vector3 o = new Vector3(origin.x, origin.y + h, origin.z);
            var hits = Physics.RaycastAll(o, dir, 2.4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            foreach (var hit in hits)
            {
                if (!hit.collider || hit.collider.isTrigger) continue;
                if (hit.distance < gap) gap = hit.distance;
            }
        }
        return gap;
    }

    /// <summary>从候选点正上方往下打，取“像地板”的那一层（不是柜顶那一小块）。</summary>
    static float GroundHeight(Vector3 point, float referenceY)
    {
        var hits = Physics.RaycastAll(point + Vector3.up * 2.2f, Vector3.down, 4.4f,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float best = referenceY;
        float bestGap = float.MaxValue;
        foreach (var hit in hits)
        {
            if (!hit.collider || hit.collider.isTrigger) continue;
            // 优先取比候选点低、又离参考高度最近的那一层：地面/楼板，而不是压在上面的小家具
            float y = hit.point.y;
            if (y > referenceY + .35f) continue;
            float gap = Mathf.Abs(y - referenceY);
            if (gap < bestGap) { bestGap = gap; best = y; }
        }
        return best;
    }

    // ---------------------------------------------------------------- 组装

    void Build()
    {
        Shader ringShader = Resources.Load<Shader>("Portal/PortalRing");
        Shader discShader = Resources.Load<Shader>("Portal/PortalSurface");
        Shader sparkShader = Resources.Load<Shader>("Portal/PortalSpark");

        // 光幕
        var discGo = new GameObject("光幕");
        discGo.transform.SetParent(transform, false);
        discGo.transform.localPosition = new Vector3(0f, RingCenterY, 0f);
        discGo.transform.localScale = new Vector3(MajorRadius, MajorRadius, 1f) * .96f;
        discGo.AddComponent<MeshFilter>().sharedMesh = DiscMesh(64);
        var discR = discGo.AddComponent<MeshRenderer>();
        discR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        discR.receiveShadows = false;
        discMat = new Material(discShader ? discShader : RuntimeShaders.Unlit);
        discMat.SetFloat("_Intensity", 1f);
        discMat.SetFloat("_Speed", .5f);
        discR.sharedMaterial = discMat;
        disc = discGo.transform;

        // 光环（内外两层，外层更粗更淡，做出“辉光”）
        ring = new GameObject("光环").transform;
        ring.SetParent(transform, false);
        ring.localPosition = new Vector3(0f, RingCenterY, 0f);
        ringMat = new Material(ringShader ? ringShader : RuntimeShaders.Unlit);
        ringMat.SetFloat("_Speed", 2.2f);
        ringMat.SetFloat("_Intensity", 1f);
        AddTorus(ring, MajorRadius, Tube, ringMat, "光环本体");
        var halo = new Material(ringMat);
        halo.SetFloat("_Intensity", .42f);
        AddTorus(ring, MajorRadius, Tube * 2.6f, halo, "光环外辉");

        // 绕着环慢慢转的小光点
        shards = new GameObject("环上流光").transform;
        shards.SetParent(ring, false);
        var shardMat = new Material(sparkShader ? sparkShader : RuntimeShaders.Unlit);
        shardMat.SetColor("_Color", new Color(1f, .88f, .62f, 1f));
        for (int i = 0; i < 10; i++)
        {
            float a = i / 10f * Mathf.PI * 2f;
            var s = GameObject.CreatePrimitive(PrimitiveType.Quad);
            s.name = "流光" + i;
            Destroy(s.GetComponent<Collider>());
            s.transform.SetParent(shards, false);
            s.transform.localPosition = new Vector3(Mathf.Cos(a) * MajorRadius, Mathf.Sin(a) * MajorRadius, 0f);
            s.transform.localScale = Vector3.one * (i % 3 == 0 ? .16f : .10f);
            var r = s.GetComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.sharedMaterial = shardMat;
        }

        // 光点粒子：从环上往里飘
        var psGo = new GameObject("光尘");
        psGo.transform.SetParent(transform, false);
        psGo.transform.localPosition = new Vector3(0f, RingCenterY, 0f);
        sparks = psGo.AddComponent<ParticleSystem>();
        var main = sparks.main;
        main.loop = true;
        main.duration = 2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 1.9f);
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(.035f, .085f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, .87f, .58f, 1f), new Color(1f, .70f, .30f, 1f));
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 220;
        main.prewarm = true;

        var emission = sparks.emission;
        emission.rateOverTime = 46f;

        var shape = sparks.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = MajorRadius * .94f;
        shape.radiusThickness = .12f;
        shape.arc = 360f;

        var vel = sparks.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.Local;
        vel.radial = new ParticleSystem.MinMaxCurve(-.55f, -.22f);
        vel.y = new ParticleSystem.MinMaxCurve(-.05f, .05f);

        var size = sparks.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, .1f));

        var color = sparks.colorOverLifetime;
        color.enabled = true;
        var grad = new Gradient();
        grad.SetKeys(
            new[] { new GradientColorKey(new Color(1f, .95f, .8f), 0f), new GradientColorKey(new Color(1f, .68f, .28f), 1f) },
            new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(.95f, .25f), new GradientAlphaKey(.75f, .7f), new GradientAlphaKey(0f, 1f) });
        color.color = new ParticleSystem.MinMaxGradient(grad);

        var psr = psGo.GetComponent<ParticleSystemRenderer>();
        psr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        psr.receiveShadows = false;
        sparkMat = new Material(sparkShader ? sparkShader : RuntimeShaders.Unlit);
        sparkMat.SetColor("_Color", new Color(1f, .86f, .58f, 1f));
        psr.sharedMaterial = sparkMat;

        // 门框的光
        var lightGo = new GameObject("门光");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.localPosition = new Vector3(0f, RingCenterY, .30f);
        glow = lightGo.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = new Color(1f, .78f, .42f);
        glow.range = 5.5f;
        glow.intensity = 0f;
        glow.shadows = LightShadows.None;

        // 落脚处的地面光斑：远看就知道“那儿有一道门”，走近也不至于踩不到门槛
        var floorGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        floorGo.name = "地面光斑";
        Destroy(floorGo.GetComponent<Collider>());
        floorGo.transform.SetParent(transform, false);
        floorGo.transform.localPosition = new Vector3(0f, .018f, .10f);
        floorGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        floorGo.transform.localScale = Vector3.one * (MajorRadius * 2.35f);
        var floorR = floorGo.GetComponent<MeshRenderer>();
        floorR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        floorR.receiveShadows = false;
        floorMat = new Material(sparkShader ? sparkShader : RuntimeShaders.Unlit);
        floorMat.SetColor("_Color", new Color(1f, .80f, .44f, .60f));
        floorMat.SetFloat("_Boost", .85f);
        floorR.sharedMaterial = floorMat;

        // 触发区：站在门口任意高度都会碰到
        var trigger = gameObject.AddComponent<SphereCollider>();
        trigger.isTrigger = true;
        trigger.center = new Vector3(0f, 1.0f, 0f);
        trigger.radius = .82f;

        bornAt = Time.time;
        transform.localScale = Vector3.one * .12f;
        StartCoroutine(OpenRoutine());
    }

    void AddTorus(Transform parent, float major, float tube, Material mat, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = TorusMesh(major, tube, 96, 16);
        var r = go.AddComponent<MeshRenderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.sharedMaterial = mat;
    }

    IEnumerator OpenRoutine()
    {
        GameAudio.PlayPortal();
        float t = 0f;
        while (t < .95f)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / .95f);
            float e = 1f - Mathf.Pow(1f - k, 3f);          // 先快后慢地“弹”开
            transform.localScale = Vector3.one * Mathf.Lerp(.12f, 1f, e);
            if (glow) glow.intensity = baseGlow * e;
            yield return null;
        }
        transform.localScale = Vector3.one;
        if (glow) glow.intensity = baseGlow;

        // 再等半秒才“通电”：刚生成的一瞬间玩家可能正站在门框里（放置点就在旁边），
        // 这一下延迟是为了让门先长出来、玩家看清它，再自己去走过去。
        float w = 0f;
        while (w < .55f) { w += Time.unscaledDeltaTime; yield return null; }
        armed = true;
    }

    void Update()
    {
        float t = Time.time - bornAt;
        if (ring)
        {
            // 环整体极缓慢地转动，配合流光看起来有“能量在跑”
            ring.localRotation = Quaternion.Euler(0f, 0f, t * 9f);
            float breathe = 1f + Mathf.Sin(t * 1.7f) * .012f;
            ring.localScale = new Vector3(breathe, breathe, 1f);
        }
        if (disc && discMat)
        {
            disc.localRotation = Quaternion.Euler(0f, 0f, -t * 3.5f);
            discMat.SetFloat("_Intensity", .92f + Mathf.Sin(t * 2.3f) * .08f);
        }
        if (glow) glow.intensity = baseGlow * (.88f + Mathf.Sin(t * 2.1f) * .12f);
        if (floorMat) floorMat.SetFloat("_Boost", .78f + Mathf.Sin(t * 1.9f) * .14f);
    }

    // ---------------------------------------------------------------- 进门

    void OnTriggerEnter(Collider other) { TryEnter(other); }

    // 玩家如果刚好在门“通电”那一刻已经站在门框里，Enter 事件不会再触发一次，
    // 用 Stay 兜住这种情况：走进去、停一下，同样会过场。
    void OnTriggerStay(Collider other) { TryEnter(other); }

    void TryEnter(Collider other)
    {
        if (entering || !armed) return;
        if (!other || !other.GetComponentInParent<PlayerController>()) return;
        Enter();
    }

    /// <summary>走进光门：整屏化成暖白光，同时加载下一关。</summary>
    public void Enter()
    {
        if (entering) return;
        entering = true;

        // 交出去之后玩家就不能再动了，免得在淡出期间撞到别的东西
        var pc = FindObjectOfType<PlayerController>();
        if (pc) pc.enabled = false;

        ScreenFader.ToScene(nextScene, new Color(1f, .94f, .82f), 1.15f, 1.25f);
    }

    // ---------------------------------------------------------------- 网格

    static Mesh TorusMesh(float major, float minor, int segU, int segV)
    {
        var verts = new List<Vector3>();
        var norms = new List<Vector3>();
        var uvs = new List<Vector2>();
        var tris = new List<int>();

        for (int i = 0; i <= segU; i++)
        {
            float u = i / (float)segU;
            float a = u * Mathf.PI * 2f;
            Vector3 center = new Vector3(Mathf.Cos(a) * major, Mathf.Sin(a) * major, 0f);
            for (int j = 0; j <= segV; j++)
            {
                float v = j / (float)segV;
                float b = v * Mathf.PI * 2f;
                Vector3 n = new Vector3(Mathf.Cos(a) * Mathf.Cos(b), Mathf.Sin(a) * Mathf.Cos(b), Mathf.Sin(b));
                verts.Add(center + n * minor);
                norms.Add(n);
                uvs.Add(new Vector2(u, v));
            }
        }
        for (int i = 0; i < segU; i++)
        {
            for (int j = 0; j < segV; j++)
            {
                int i0 = i * (segV + 1) + j;
                int i1 = (i + 1) * (segV + 1) + j;
                tris.Add(i0); tris.Add(i1); tris.Add(i0 + 1);
                tris.Add(i0 + 1); tris.Add(i1); tris.Add(i1 + 1);
            }
        }

        var mesh = new Mesh { name = "PortalTorus" };
        mesh.SetVertices(verts);
        mesh.SetNormals(norms);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    static Mesh DiscMesh(int segments)
    {
        var verts = new List<Vector3> { Vector3.zero };
        var uvs = new List<Vector2> { new Vector2(.5f, .5f) };
        var tris = new List<int>();
        for (int i = 0; i <= segments; i++)
        {
            float a = i / (float)segments * Mathf.PI * 2f;
            float x = Mathf.Cos(a), y = Mathf.Sin(a);
            verts.Add(new Vector3(x, y, 0f));
            uvs.Add(new Vector2(.5f + x * .5f, .5f + y * .5f));
            if (i > 0)
            {
                tris.Add(0); tris.Add(i); tris.Add(i + 1);
            }
        }
        var mesh = new Mesh { name = "PortalDisc" };
        mesh.SetVertices(verts);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}
