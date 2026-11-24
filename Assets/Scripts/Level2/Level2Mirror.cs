using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 谜题三 · 闭环问答（客厅）。
/// 用的是一楼客厅里那面本来就有的旧穿衣镜（sm_forgottenmirror）：把它的镜面换成
/// 一块实时反射面（镜像相机 + RenderTexture），玩家站到镜前能看见自己 —— 那个还没长大的自己。
/// 镜前问答选完 → 桌上那个盒子自己弹开 → 打开盒子读信 → 黑幕白字收束。
/// </summary>
public sealed class Level2Mirror : MonoBehaviour
{
    Level2Ui Ui => Level2Director.Instance ? Level2Director.Instance.Ui : null;

    Transform mirrorRoot;
    Transform disc, adultDisc;
    Renderer discRenderer;
    Renderer rimRenderer;
    Renderer[] mirrorRenderers = new Renderer[0];
    bool[] mirrorRendererState = new bool[0];
    Camera mirrorCam;
    RenderTexture rt;
    Material discMaterial, adultMaterial;
    Level2MirrorTalk talk;
    Level2LetterBox box;
    ProximityOutline boxOutline;
    SkinnedMeshRenderer bodyShadow, bodySkin;

    Vector3 mirrorCenter, mirrorNormal;
    float halfW = .21f, halfH = .58f;

    bool unlocked, talked, answered, letterOpened, finished, adultShown, saidSomething;
    bool loggedRender;

    /// <summary>斜切面法线方向（相对镜面法线）：0 未定，+1 / -1 已定。只算一次。</summary>
    int clipSign;

    /// <summary>镜子前那句“想对小时候的自己说的话”，未解锁时也能说，和任务用的 answered 分开记。</summary>
    static readonly string[] SelfWords =
    {
        "我没能一直开心，但我好好走到现在了",
        "谢谢你当年的满怀期待，让我来到了这里",
    };
    public bool Unlocked => unlocked;
    public bool Answered => answered;

    /// <summary>未解锁时在镜子前说过心里话没有（说过就不再给交互提示，免得按 E 毫无反应）。</summary>
    public bool SaidSomething => saidSomething;

    public void Setup()
    {
        BuildMirror();
        BuildBox();
    }

    public void Unlock()
    {
        unlocked = true;
        Debug.Log("[谜题三] 已解锁");
    }

    // ---------------- 客厅那面旧穿衣镜 ----------------

    /// <summary>
    /// 上一版运行实测到的镜面世界数据（Editor-prev.log：[谜题三] 穿衣镜 中心=(-24.26,1.67,-95.42)
    /// 法线=(0.99,0.12,-0.04) 尺寸=0.51×1.46）。场景没动过，所以这组值可以直接当兜底 ——
    /// 宁可写死，也不能让玩家走到镜子前既看不见自己、按 E 又没有任何反应。
    /// </summary>
    static readonly Vector3 MirrorCenterHint = new Vector3(-24.262f, 1.671f, -95.418f);
    static readonly Vector3 MirrorNormalHint = new Vector3(.991f, .122f, -.041f);
    const float MirrorHalfWHint = .255f, MirrorHalfHHint = .730f;

    void BuildMirror()
    {
        mirrorRoot = FindMirrorRoot();
        CacheMirrorRenderers();

        if (!mirrorRoot || !MeasureMirror())
        {
            Debug.LogWarning($"[谜题三] 没量到镜面网格（{(mirrorRoot ? mirrorRoot.name : "没找到")}），" +
                             "改用上一版的实测值兜底，反射和交互照建");
            mirrorCenter = MirrorCenterHint;
            mirrorNormal = MirrorNormalHint.normalized;
            halfW = MirrorHalfWHint;
            halfH = MirrorHalfHHint;
            // 注意：**不要**把 mirrorRoot 清空。反射渲染时要靠它把整面镜子（木框 + 玻璃）
            // 一起藏起来，否则镜后相机会把木框背面拍进镜子里，就是那块棕色木头。
        }

        // 反射面：一块椭圆薄片，正好盖在原来的镜面上。
        // 偏移从 6mm 提到 10mm：兜底模式下原镜面网格没被隐藏，得让它稳稳压在后面。
        int w = 512;
        int h = Mathf.RoundToInt(w * (halfH * 2f) / Mathf.Max(.01f, halfW * 2f));
        h = Mathf.Clamp(h, 256, 2048);
        rt = new RenderTexture(w, h, 24, RenderTextureFormat.Default) { hideFlags = HideFlags.DontSave };
        rt.Create();

        discMaterial = L2.Unlit(Color.white);
        discMaterial.SetTexture("_BaseMap", rt);
        // 镜面 quad 的 +X 与玩家右手相反，贴图水平翻一次才是正着的镜子。
        discMaterial.SetTextureScale("_BaseMap", new Vector2(-1f, 1f));
        discMaterial.SetTextureOffset("_BaseMap", new Vector2(1f, 0f));

        disc = L2.EllipseDisc("镜面", null, mirrorCenter + mirrorNormal * .010f, mirrorNormal,
            halfW * .985f, halfH * .985f, discMaterial);
        discRenderer = disc.GetComponent<Renderer>();
        discRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        // 镜子里“长大之后”的那个身影：一层贴在镜面前的剪影，默认全透明。
        adultMaterial = L2.Unlit(new Color(1f, 1f, 1f, 0f), transparent: true);
        var adultTex = Level2Ui.Instance ? Level2Ui.Instance.AdultFigure : null;
        if (adultTex)
        {
            adultMaterial.SetTexture("_BaseMap", adultTex);
            adultMaterial.SetTextureScale("_BaseMap", new Vector2(1f, 1f));
        }
        adultDisc = L2.EllipseDisc("镜中人影", null, mirrorCenter + mirrorNormal * .012f, mirrorNormal,
            halfW * .72f, halfH * .80f, adultMaterial);
        adultDisc.gameObject.SetActive(false);

        // 交互盒：站在镜子前按 E 就能触发。
        var host = new GameObject("落地镜·交互");
        host.transform.SetPositionAndRotation(mirrorCenter + mirrorNormal * .55f,
            Quaternion.LookRotation(mirrorNormal, Vector3.up));
        var col = host.AddComponent<BoxCollider>();
        col.size = new Vector3(halfW * 2f + .50f, halfH * 2f, .80f);

        talk = host.AddComponent<Level2MirrorTalk>();
        talk.host = this;

        // 客厅是整间屋子情绪的落点：给镜前一小片柔和暖光，别让这里冷得像走廊尽头。
        var glow = new GameObject("客厅暖光");
        glow.transform.position = mirrorCenter + mirrorNormal * .7f + Vector3.up * .75f;
        var lamp = glow.AddComponent<Light>();
        lamp.type = LightType.Point;
        lamp.color = new Color(1f, .82f, .60f);
        lamp.range = 3.2f;
        lamp.intensity = .55f;
        lamp.shadows = LightShadows.Soft;

        BuildMirrorCamera();

        // 大厅里多出来的这面落地镜：沿镜面轮廓一线很淡的蓝，玩家扫一眼就能注意到。
        BuildRimGlow();

        Debug.Log($"[谜题三] 穿衣镜 中心={L2.Fmt(mirrorCenter)} 法线={L2.Fmt(mirrorNormal)} 尺寸={halfW * 2f:F2}×{halfH * 2f:F2}");
    }

    /// <summary>
    /// 找出真正带镜面网格的那一层。
    ///
    /// sm_forgottenmirror 是 FBX 直接实例化的：根节点和网格子物体同名，而且根上还挂着一个
    /// **sharedMesh 为空的 MeshRenderer 空壳**。以前 L2.Find 按名字取第一个，抓到空壳就
    /// 在 MeasureMirror 里 `!mf.sharedMesh` 直接失败 —— 镜面贴片、反射相机、交互盒全都不建，
    /// 表现就是镜子黑着、按 E 没反应，而且抓对抓错看运行顺序。
    ///
    /// 现在：只在真正有 mesh 的节点里挑，优先材质名含 mirror，其次位置对得上；
    /// 名字完全对不上时，退到全场景按“材质叫 mirror + 就在这面镜子位置附近”捞。
    /// </summary>
    static Transform FindMirrorRoot()
    {
        Transform best = null;
        int bestScore = int.MinValue;
        int seen = 0;

        foreach (var t in L2.FindContains("forgottenmirror"))
        {
            if (!t) continue;
            foreach (var r in t.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!r) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (!mf || !mf.sharedMesh) continue;       // 空壳 renderer，跳过
                seen++;

                int score = 2;                              // 有真网格
                if (HasMirrorMaterial(r)) score += 6;
                if ((r.bounds.center - MirrorCenterHint).sqrMagnitude < 9f) score += 3;

                if (score > bestScore) { bestScore = score; best = r.transform; }
            }
        }

        if (best)
        {
            Debug.Log($"[谜题三] 镜子节点 {seen} 个候选，选中 {best.name}（{L2.Fmt(best.position)}）");
            return best;
        }

        // 名字全对不上：整个场景按“材质叫 mirror + 就在这面镜子该在的位置”捞一遍。
        foreach (var r in FindObjectsOfType<MeshRenderer>(true))
        {
            if (!HasMirrorMaterial(r)) continue;
            var mf = r.GetComponent<MeshFilter>();
            if (!mf || !mf.sharedMesh) continue;
            if ((r.bounds.center - MirrorCenterHint).sqrMagnitude > 20f) continue;
            Debug.Log($"[谜题三] 按材质捞到镜面 {r.name}（{L2.Fmt(r.transform.position)}）");
            return r.transform;
        }

        return null;
    }

    static bool HasMirrorMaterial(Renderer r)
    {
        if (!r) return false;
        foreach (var m in r.sharedMaterials)
            if (m && m.name.IndexOf("mirror", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        return false;
    }

    /// <summary>
    /// 把整面镜子的渲染器（木框 + 镜面玻璃，FBX 里是两个 submesh，也可能拆在几个节点上）
    /// 全缓存下来。渲染反射的那一瞬要全部关掉 —— 只要漏掉一个，镜后相机就会把它拍进镜子里：
    /// 木框背面就是那块挡在人物腿上的棕色木头。
    /// </summary>
    void CacheMirrorRenderers()
    {
        mirrorRenderers = new Renderer[0];
        mirrorRendererState = new bool[0];
        if (!mirrorRoot) return;

        // 从选中的节点往上爬到 FBX 实例的根（同名的那一层），整个模型的渲染器都要算进来。
        Transform top = mirrorRoot;
        while (top.parent &&
               top.parent.name.IndexOf("forgottenmirror", System.StringComparison.OrdinalIgnoreCase) >= 0)
            top = top.parent;

        mirrorRenderers = top.GetComponentsInChildren<Renderer>(true);
        mirrorRendererState = new bool[mirrorRenderers.Length];
    }

    /// <summary>从镜子里那块 “mirror” 材质的面片量出镜面的中心、朝向和大小。</summary>
    bool MeasureMirror()
    {
        var mf = mirrorRoot.GetComponent<MeshFilter>();
        var mr = mirrorRoot.GetComponent<MeshRenderer>();
        if (!mf || !mr || !mf.sharedMesh) return false;

        var mesh = mf.sharedMesh;
        var mats = mr.sharedMaterials;
        int sub = -1;
        for (int i = 0; i < mats.Length; i++)
            if (mats[i] && mats[i].name.ToLowerInvariant().Contains("mirror")) { sub = i; break; }
        if (sub < 0) sub = mesh.subMeshCount - 1;
        // 材质数可能比 submesh 多（多出来的材质不参与渲染），索引必须夹回合法范围，
        // 否则 GetTriangles 会抛 ArgumentOutOfRange，把后面的盒子、交互一起带崩。
        if (sub < 0 || sub >= mesh.subMeshCount) sub = Mathf.Max(0, mesh.subMeshCount - 1);

        var tris = mesh.GetTriangles(sub);
        var verts = mesh.vertices;
        if (tris.Length < 3) return false;

        var idx = new HashSet<int>();
        for (int i = 0; i < tris.Length; i++) idx.Add(tris[i]);

        Vector3 localCenter = Vector3.zero;
        foreach (var i in idx) localCenter += verts[i];
        localCenter /= Mathf.Max(1, idx.Count);

        Vector3 a = mirrorRoot.TransformPoint(verts[tris[0]]);
        Vector3 b = mirrorRoot.TransformPoint(verts[tris[1]]);
        Vector3 c = mirrorRoot.TransformPoint(verts[tris[2]]);
        // 前三个顶点可能是退化三角形，取投影面积最大的那一片。
        Vector3 nrm = Vector3.Cross(b - a, c - a);
        float bestArea = nrm.sqrMagnitude;
        for (int i = 0; i + 2 < tris.Length; i += 3)
        {
            Vector3 p0 = mirrorRoot.TransformPoint(verts[tris[i]]);
            Vector3 p1 = mirrorRoot.TransformPoint(verts[tris[i + 1]]);
            Vector3 p2 = mirrorRoot.TransformPoint(verts[tris[i + 2]]);
            Vector3 cr = Vector3.Cross(p1 - p0, p2 - p0);
            if (cr.sqrMagnitude > bestArea) { bestArea = cr.sqrMagnitude; nrm = cr; }
        }

        mirrorCenter = mirrorRoot.TransformPoint(localCenter);
        if (nrm.sqrMagnitude < 1e-8f)
        {
            Debug.LogWarning("[谜题三] 镜面网格太扁，算不出法线");
            return false;
        }
        nrm.Normalize();

        // 让法线朝向客厅（玩家站的那一边）
        Vector3 viewer = Level2Director.Instance && Level2Director.Instance.Player
            ? Level2Director.Instance.Player.position
            : mirrorCenter + Vector3.right;
        if (Vector3.Dot(nrm, viewer - mirrorCenter) < 0f) nrm = -nrm;
        mirrorNormal = nrm;

        Vector3 R = Vector3.Cross(Vector3.up, mirrorNormal).normalized;
        Vector3 U = Vector3.Cross(mirrorNormal, R).normalized;

        float minR = float.MaxValue, maxR = float.MinValue, minU = float.MaxValue, maxU = float.MinValue;
        foreach (var i in idx)
        {
            Vector3 p = mirrorRoot.TransformPoint(verts[i]);
            float r = Vector3.Dot(p - mirrorCenter, R);
            float u = Vector3.Dot(p - mirrorCenter, U);
            minR = Mathf.Min(minR, r); maxR = Mathf.Max(maxR, r);
            minU = Mathf.Min(minU, u); maxU = Mathf.Max(maxU, u);
        }

        halfW = Mathf.Max(.05f, (maxR - minR) * .5f);
        halfH = Mathf.Max(.05f, (maxU - minU) * .5f);
        mirrorCenter += R * ((minR + maxR) * .5f) + U * ((minU + maxU) * .5f);
        return true;
    }

    void BuildMirrorCamera()
    {
        var go = new GameObject("镜面反射相机");
        mirrorCam = go.AddComponent<Camera>();
        mirrorCam.enabled = false;                  // 只在需要时手动 Render
        mirrorCam.targetTexture = rt;
        mirrorCam.nearClipPlane = .05f;
        mirrorCam.farClipPlane = 50f;
        mirrorCam.clearFlags = CameraClearFlags.SolidColor;
        mirrorCam.backgroundColor = new Color(.04f, .045f, .055f);
        go.transform.SetParent(transform, false);
    }

    /// <summary>
    /// 把“镜面所在的世界平面”换算到镜面相机的相机空间，交给 CalculateObliqueMatrix 当近裁剪面。
    /// sideSign = 1：保留平面法线指向的那一侧（也就是客厅），镜子背后整片切掉。
    /// </summary>
    static Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal, float sideSign)
    {
        Matrix4x4 m = cam.worldToCameraMatrix;
        Vector3 cpos = m.MultiplyPoint(pos);
        Vector3 cnormal = m.MultiplyVector(normal).normalized * sideSign;
        return new Vector4(cnormal.x, cnormal.y, cnormal.z, -Vector3.Dot(cpos, cnormal));
    }

    /// <summary>
    /// 斜切面该保留哪一侧，不靠人肉推公式 —— Unity 的裁剪空间是反 Z，符号写反了
    /// 会把整面镜子切空（镜子里全黑），而且这种错很难在代码里一眼看出来。
    ///
    /// 做法：拿“镜子正前方 25cm 的一小块空气”当探针（它一定属于客厅），
    /// 把探针投到裁剪空间，只看**近/远裁剪面**：可见必须满足 w > 0 且 -w ≤ z ≤ w。
    /// 第一遍不满足就说明客厅那一侧被切掉了，把法线翻过来重算。
    ///
    /// 为什么不用 GeometryUtility 判整台视锥：镜面 RT 是 512×1459 的竖条，
    /// 横向 FOV 只有 9° 左右，稍微偏一点就会被左右两侧的平面误判成“不可见”。
    /// </summary>
    int ResolveClipSign(Vector3 planePos, Vector3 N)
    {
        Vector3 probe = mirrorCenter + N * .25f;
        Vector4 probeView = mirrorCam.worldToCameraMatrix *
                            new Vector4(probe.x, probe.y, probe.z, 1f);

        for (int i = 0; i < 2; i++)
        {
            float sign = i == 0 ? 1f : -1f;
            // 每次都先回到标准投影再算斜切：否则第二遍是在上一遍的斜切矩阵上再切一刀。
            mirrorCam.ResetProjectionMatrix();
            Matrix4x4 oblique = mirrorCam.CalculateObliqueMatrix(CameraSpacePlane(mirrorCam, planePos, N, sign));
            mirrorCam.projectionMatrix = oblique;

            Vector4 clip = oblique * probeView;
            if (clip.w > 0f && clip.z >= -clip.w && clip.z <= clip.w)
            {
                Debug.Log($"[谜题三] 镜面斜切方向已定：{(sign > 0 ? "法线正向" : "法线反向")}（保留客厅侧）");
                return sign > 0 ? 1 : -1;
            }
        }

        return 1;
    }

    /// <summary>
    /// 镜框**外侧**那一圈淡蓝，用来在大厅里认出“多出来的这面镜子”。
    ///
    /// 两个必须守住的点：
    /// 1）整圈都要落在镜面玻璃之外（内圈半径也在 halfW/halfH 的 1.3 倍以上），
    ///    否则从正面看就像镜子里镶了一道蓝边；
    /// 2）渲染镜面反射时要把它藏起来（见 rimRenderer 在 LateUpdate 里的处理），
    ///    它是双面材质，不藏就会被镜后相机原样拍进反射画面。
    ///
    /// 不能用 ProximityOutline：那是沿法线外扩、Cull Front 的壳，对薄片会把整块椭圆填成暗蓝。
    /// </summary>
    void BuildRimGlow()
    {
        const int seg = 80;
        // 木框比镜面玻璃宽出一圈：横向约 1.44 倍、纵向约 1.16 倍，实测截图估的。
        float rx = halfW * 1.44f, ry = halfH * 1.16f;
        float band = Mathf.Clamp(halfW * .062f, .010f, .018f);
        float ix = Mathf.Max(rx * .90f, rx - band), iy = Mathf.Max(ry * .90f, ry - band);

        var verts = new List<Vector3>();
        var tris = new List<int>();
        for (int i = 0; i <= seg; i++)
        {
            float a = i / (float)seg * Mathf.PI * 2f;
            float cx = Mathf.Cos(a), cy = Mathf.Sin(a);
            verts.Add(new Vector3(cx * rx, cy * ry, 0f));   // 外圈
            verts.Add(new Vector3(cx * ix, cy * iy, 0f));   // 内圈
        }
        for (int i = 0; i < seg; i++)
        {
            int o = i * 2;
            tris.Add(o); tris.Add(o + 1); tris.Add(o + 2);
            tris.Add(o + 1); tris.Add(o + 3); tris.Add(o + 2);
        }

        var mesh = new Mesh { name = "镜面轮廓_mesh" };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var go = new GameObject("镜面轮廓蓝边");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        var mat = L2.Unlit(new Color(.42f, .72f, 1f, .30f), transparent: true);
        if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        rimRenderer = mr;

        go.transform.SetParent(transform, true);
        // 木框本身有 2–3cm 的厚度，环带要压到框前面才不会嵌进去；半径已经大于镜面，不会进镜子。
        go.transform.SetPositionAndRotation(mirrorCenter + mirrorNormal * .030f,
            Quaternion.LookRotation(mirrorNormal, Vector3.up));
    }

    void LateUpdate()
    {
        if (!mirrorCam || !disc || !Camera.main) return;
        var viewer = Camera.main.transform;
        if ((viewer.position - mirrorCenter).sqrMagnitude > 400f) return;   // 离得远就不渲染

        Vector3 N = mirrorNormal;
        float side = Vector3.Dot(N, viewer.position - mirrorCenter);
        if (side < .05f) return;                                            // 人不在镜子正面

        // 镜子该显示的是“玩家眼睛看到的那一面”：把玩家相机整个镜过去。
        Vector3 camPos = mirrorCenter + Vector3.Reflect(viewer.position - mirrorCenter, N);

        // 视线别照着镜子的法线走：这面穿衣镜是往前倾的，纯反射轴会把人顶出画面。
        // 直接瞄准玩家本人的包围盒中心 —— 角色的动画姿态会让身体偏离根节点十几到三十厘米，
        // 用根节点当目标会让镜子里的自己一直贴在镜边。
        if (!bodySkin) bodySkin = FindBodySkin();
        Vector3 target = bodySkin ? bodySkin.bounds.center : viewer.position;
        Vector3 aim = new Vector3(target.x, Mathf.Lerp(target.y, viewer.position.y - .30f, .35f), target.z);
        Vector3 dir = aim - camPos;
        if (dir.sqrMagnitude < .001f) return;
        mirrorCam.transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(dir, Vector3.up));

        // 框住整块镜面，再往里收一点：镜面窄，人物在镜子里才不至于太小。
        float dist = Mathf.Max(.20f, Vector3.Distance(camPos, mirrorCenter));
        float vfov = 2f * Mathf.Atan(halfH * .84f / dist) * Mathf.Rad2Deg;
        mirrorCam.fieldOfView = Mathf.Clamp(vfov, 25f, 95f);
        mirrorCam.nearClipPlane = .03f;

        // 关键：用真正的斜投影把近裁剪面“贴”在镜面上。
        //
        // 以前的写法是把 nearClipPlane 沿视线推到 dist-.04 —— 但视线是斜着看镜子的，
        // 推出来的那个平面和镜面根本不是一个方向：人越靠近，平面越歪，
        // 镜子背后的东西（墙外的草地、木框背面）就一片片漏进镜子里。
        // CalculateObliqueMatrix 直接把近裁剪面钉死在镜面所在的平面上，
        // 镜面之后的一切（墙、屋外地形、木框背面）全部被切掉，镜子里只剩客厅和玩家。
        Vector3 clipPos = mirrorCenter - N * .02f;      // 往镜后让 2cm，镜脚那一点地板不被切掉
        if (clipSign == 0) clipSign = ResolveClipSign(clipPos, N);
        mirrorCam.ResetProjectionMatrix();              // 每次从标准投影重算，别在上一帧的斜切上接着切
        mirrorCam.projectionMatrix =
            mirrorCam.CalculateObliqueMatrix(CameraSpacePlane(mirrorCam, clipPos, N, clipSign));

        // 渲染反射时必须把整面镜子自己藏起来，否则镜后相机会先撞上镜框和镜面的背面。
        // 整棵子树一起关：只关 mirrorRoot 那一个 Renderer 是不够的，木框背面会被拍成棕色木板。
        for (int i = 0; i < mirrorRenderers.Length; i++)
        {
            var r = mirrorRenderers[i];
            if (!r) continue;
            mirrorRendererState[i] = r.enabled;
            r.enabled = false;
        }
        if (discRenderer) discRenderer.enabled = false;
        // 环带也得藏：它是双面材质，镜后相机会把它原样拍进反射里，镜子里就多出一道蓝边。
        if (rimRenderer) rimRenderer.enabled = false;
        if (adultDisc) adultDisc.gameObject.SetActive(false);

        // 第一人称把头藏了，镜子里只会剩一个没有头的身体。借“只有影子”的那份完整网格，
        // 只在镜像相机渲染的这一瞬把它露出来 —— 主相机永远看不到它。
        if (!bodyShadow) bodyShadow = FindBodyShadow();
        var prevMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        if (bodyShadow)
        {
            prevMode = bodyShadow.shadowCastingMode;
            bodyShadow.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }

        mirrorCam.Render();

        if (!loggedRender)
        {
            loggedRender = true;
            Debug.Log($"[谜题三] 镜面反射开始渲染：相机 {L2.Fmt(camPos)}，FOV {mirrorCam.fieldOfView:F0}，" +
                      $"斜切面已贴镜面，藏起 {mirrorRenderers.Length} 个镜子渲染器，RT {rt.width}×{rt.height}");
        }

        if (bodyShadow) bodyShadow.shadowCastingMode = prevMode;
        for (int i = 0; i < mirrorRenderers.Length; i++)
            if (mirrorRenderers[i]) mirrorRenderers[i].enabled = mirrorRendererState[i];
        if (discRenderer) discRenderer.enabled = true;
        if (rimRenderer) rimRenderer.enabled = true;
        if (adultDisc && adultShown) adultDisc.gameObject.SetActive(true);
    }

    /// <summary>玩家身上那份“完整全身（含头）”的纯阴影渲染器，由 FullBodyShadow 生成。</summary>
    SkinnedMeshRenderer FindBodyShadow()
    {
        var player = Level2Director.Instance ? Level2Director.Instance.Player : null;
        if (!player) return null;
        foreach (var r in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (r && r.gameObject.name == "Full character shadow") return r;
        return null;
    }

    /// <summary>玩家自己的蒙皮网格（不含那份纯阴影副本），用来量他到底站在哪。</summary>
    SkinnedMeshRenderer FindBodySkin()
    {
        var player = Level2Director.Instance ? Level2Director.Instance.Player : null;
        if (!player) return null;
        foreach (var r in player.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (r && r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) return r;
        return null;
    }

    // ---------------- 桌上的盒子 ----------------

    void BuildBox()
    {
        // 客厅圆桌上那个盒子：场景里的 GameObject 叫 SM_Box_A15 (1)，
        // 网格/预制体才叫 SM_Box_A4 —— 之前按 SM_Box_A4 找是找不到的，所以盒子一直没有交互。
        Vector3 want = new Vector3(-22.472f, .894f, -98.874f);
        Transform target = L2.Find("SM_Box_A15 (1)");

        if (!target)
        {
            // 名字被改过 / 复制体加了后缀时，按位置找离圆桌最近的那个盒子。
            float best = float.MaxValue;
            foreach (var t in L2.FindContains("SM_Box"))
            {
                if (!t) continue;
                float d = (t.position - want).sqrMagnitude;
                if (d < best) { best = d; target = t; }
            }
            if (best > 1.2f) target = null;
        }

        if (!target)
        {
            Debug.LogWarning("[谜题三] 没找到客厅桌上的盒子 SM_Box_A15 (1)");
            return;
        }

        var bounds = L2.BoundsOf(target);
        var host = new GameObject("桌上的盒子·交互");
        host.transform.position = bounds.center;
        var col = host.AddComponent<BoxCollider>();
        col.size = new Vector3(Mathf.Max(.34f, bounds.size.x + .10f),
                               Mathf.Max(.26f, bounds.size.y + .12f),
                               Mathf.Max(.34f, bounds.size.z + .10f));

        box = host.AddComponent<Level2LetterBox>();
        box.host = this;
        box.visual = target;
        box.Bind(target);
        // 和玩偶、吉他同一套蓝描边（走近约 1.9m 才亮）：玩家下楼看见桌上有个亮着的东西，就知道能开。
        boxOutline = L2.AddBlueOutline(target);
        Debug.Log($"[谜题三] 桌上的盒子 {L2.Fmt(target.position)} 已加蓝色描边 {L2.Fmt(bounds.size)}");
    }

    // ---------------- 问答流程 ----------------

    public void Talk()
    {
        // 提示页还挂在屏幕上时不要再触发：否则连着按 E 会把同一段台词 / 问答页重复排进队列，
        // 表现为“看完又被问一遍”。放在最前面，未解锁那两页同样受保护。
        if (Ui && Ui.IsPageOnScreen) return;

        if (!unlocked)
        {
            // 已经说过心里话就不再重复弹页，安安静静让玩家走开。
            if (saidSomething) return;
            // 还没轮到这里的正事，先让玩家认出镜子里的自己 —— 两句独白，接着说说心里话。
            Ui?.Say("内心独白", "这就是小时候的自己吗？");
            Ui?.Say("内心独白", "原来小时候的我这么可爱、这么热爱生活。");
            AskSelfWord();
            return;
        }

        if (talked)
        {
            if (!answered) Ask();
            else Ui?.Note("镜子", "镜子里的人看着我，没有说话。桌上的盒子还开着。");
            return;
        }
        talked = true;

        // 已经在镜子前说过心里话的，就不再重复那段独白，直接进正式问答。
        if (!saidSomething) Ui?.Say("内心独白", "原来我一直在和过去的自己对视。");
        // 直接入队排在独白后面，不靠 Invoke 定时（Invoke 一旦碰上组件被禁用 / 暂停就会静默丢掉，
        // 表现就是“独白看完、问答没出来”）。队列本身保证顺序，所以这里同步调用是安全的。
        Ask();
    }

    void Ask()
    {
        if (answered) return;
        Debug.Log("[谜题三] 弹出问答页（两选一）");
        Ui?.Ask("镜子", "你现在开心吗，有什么想对此时小时候的你说？", SelfWords, OnAnswer);
    }

    /// <summary>
    /// 还没解锁谜题三时，镜子前也先问一句“想对小时候的自己说什么”。
    /// 只是说心里话，不推进任何进度 —— 所以用 saidSomething 记，不碰 answered。
    /// </summary>
    void AskSelfWord()
    {
        if (saidSomething) return;
        Debug.Log("[谜题三] 弹出“想对小时候的我说”问答页（两选一）");
        Ui?.Ask("镜子", "你现在开心吗，有什么想对此时小时候的你说？", SelfWords, OnSelfWord);
    }

    void OnSelfWord(int index)
    {
        if (saidSomething) return;
        saidSomething = true;
        Debug.Log($"[谜题三] 镜子前说的话：选项 {index + 1}");

        // 选哪一句都走同一段：镜子不评判对错，只把话题交回给玩家自己。
        Ui?.Say("镜子里", "其实不管你如何选择，你都会找到你想要的东西的！");
        Ui?.Say("小木箱", "请往旁边的桌子上看看，那儿有个小木箱，里面可能有你想要的。");
    }

    public void Answer(int index) => OnAnswer(index);

    void OnAnswer(int index)
    {
        if (answered) return;
        answered = true;
        Debug.Log("[谜题三] 玩家回答：" + index);

        Ui?.Say("镜子里", index == 0
            ? "镜子里的孩子没有哭。她只是安静地看着我，像在确认我有没有骗她。"
            : "镜子里的孩子笑了一下。那个笑我很熟悉 —— 我很久没有这样笑过了。");
        Ui?.Note("桌上的盒子", "旁边桌上那个盒子，自己弹开了一条缝。");

        if (box) box.Open();
    }

    // ---------------- 信 ----------------

    public void OnBoxOpened()
    {
        if (letterOpened) return;
        letterOpened = true;
        if (boxOutline) boxOutline.enabled = false;   // 信已经摊开，别再用蓝边指着这个空盒子

        // 信出现的那一刻，镜子里的人长大了。
        ShowAdult();

        Ui?.Letter("给小时候的我",
            "恭喜通过。\n\n" +
            "这封信不是小时候写给未来的，\n" +
            "是现在的我，写给那时候的你。\n\n" +
            "—— 长大后的你",
            OnLetterClosed);
    }

    void ShowAdult()
    {
        if (!adultDisc) return;
        adultShown = true;
        adultDisc.gameObject.SetActive(true);
        if (!adultMaterial) return;
        StopAllCoroutines();
        StartCoroutine(FadeAdult());
    }

    System.Collections.IEnumerator FadeAdult()
    {
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * .55f;
            var c = new Color(1f, 1f, 1f, Mathf.Clamp01(t));
            adultMaterial.SetColor("_BaseColor", c);
            if (adultMaterial.HasProperty("_Color")) adultMaterial.SetColor("_Color", c);
            yield return null;
        }
    }

    void OnLetterClosed()
    {
        if (finished) return;
        finished = true;
        foreach (var l in FindObjectsOfType<L2Flicker>()) l.gameObject.SetActive(false);

        // 黑幕白字：一个字一个字打出来，全部出完后才允许按空格。
        Ui?.Ending("主角内心独白", new[]
        {
            "原来当年的信，不是用来等待一个完美的未来。",
            "它是留给我的一场重逢，",
            "让长大的我，终于可以好好回应那个满怀期待的小孩。",
        }, OnEndingDone);
    }

    void OnEndingDone()
    {
        Debug.Log("[谜题三] 全部解密闭环完成");
        Ui?.Ending("—— 完 ——", new[] { "" }, null);
        StartCoroutine(QuitAfter(3.6f));
    }

    /// <summary>
    /// 信读完、内心独白播完、屏幕上打出“—— 完 ——”之后，停 3.6 秒让人看清，
    /// 然后结束游戏进程（编辑器里就是停止播放）。
    /// </summary>
    System.Collections.IEnumerator QuitAfter(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        Debug.Log("[谜题三] 游戏结束，退出");
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    // ---------------- 存档 ----------------

    /// <summary>0 未解锁 / 1 已解锁 / 2 已答完 / 3 已读信 / 4 已看完结局。</summary>
    public int Stage
    {
        get
        {
            if (finished) return 4;
            if (letterOpened) return 3;
            if (answered) return 2;
            if (unlocked) return 1;
            return 0;
        }
    }

    public void RestoreStage(int stage)
    {
        if (stage <= 0) return;
        unlocked = true;
        if (stage >= 2)
        {
            talked = true;
            answered = true;
            if (box) box.Open();
        }
        if (stage >= 3)
        {
            letterOpened = true;
            if (boxOutline) boxOutline.enabled = false;   // 读档时信已经读过了，蓝边不用再亮
            ShowAdult();
        }
        if (stage >= 4) finished = true;
        Debug.Log($"[谜题三] 读档恢复：进度 {stage}");
    }
}

/// <summary>镜子前的那一下就按 E。</summary>
public sealed class Level2MirrorTalk : L2Interactable
{
    public Level2Mirror host;

    public override string Prompt
    {
        get
        {
            if (!host) return "";
            if (!host.Unlocked) return host.SaidSomething ? "" : "E 看看镜子";
            if (host.Answered) return "E 再看一眼镜子";
            return "E 站到镜子前";
        }
    }

    public override void Interact(PlayerInventory inventory) => host?.Talk();
}

/// <summary>客厅桌上那个小盒子。问答之后自己弹开，再按 E 打开读信。</summary>
public sealed class Level2LetterBox : L2Interactable
{
    public Level2Mirror host;
    public Transform visual;
    bool popped, read;
    Vector3 restPos;
    float popT;

    public bool IsPopped => popped;

    public void Bind(Transform box)
    {
        visual = box;
        restPos = box.position;
    }

    public void Open()
    {
        if (popped) return;
        popped = true;
        popT = 0f;
        Debug.Log("[谜题三] 盒子自动弹开");
    }

    public override string Prompt
    {
        get
        {
            if (!popped) return "E 看看小木箱";
            if (read) return "信已经读过了";
            return "E 打开盒子";
        }
    }

    public override void Interact(PlayerInventory inventory)
    {
        if (!popped)
        {
            // 玩家被引导过来看盒子，但谜题三还没到时候开 —— 别按了 E 毫无反应。
            Level2Ui.Instance?.Note("小木箱", "盒盖扣得很紧，现在还打不开。");
            return;
        }
        if (read) return;
        read = true;
        host?.OnBoxOpened();
    }

    void Update()
    {
        if (!popped || popT >= 1f) return;
        // 弹开：先轻轻跳一下，再落回去 —— 没有单独的盖子网格，用整体位移表达“开了”。
        popT = Mathf.Min(1f, popT + Time.deltaTime * 2.2f);
        float bounce = Mathf.Sin(popT * Mathf.PI) * .06f;
        if (visual) visual.position = restPos + Vector3.up * bounce;
    }
}
