using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// 关卡一收尾：四个玩偶归位、剧情 UI 播完之后，
// 自动拉开书桌【中间】抽屉（模型里的 Draw 1/2/3_low 里正中间那个，沿面板法线垂直于桌体滑出），
// 再把信封碎片①（对象007）放进抽屉里，加黄色描边 + “E 拾取”提示，供玩家拾取。
[DisallowMultipleComponent]
public sealed class DrawerFragmentReveal : MonoBehaviour
{
    [Header("抽屉定位")]
    [Tooltip("场景里作为抽屉句柄的空物体名字。")]
    public string drawerHandleName = "书桌抽屉 1";
    [Tooltip("抽屉模型节点名的前缀（书桌模型里是 Draw 1_low / Draw 2_low / Draw 3_low，按网格名匹配）。")]
    public string drawerMeshName = "Draw 2_low";

    [Header("开启动画")]
    [Tooltip("整只抽屉拉到位要用的时间（秒）。")]
    public float pullDuration = 1.15f;
    [Tooltip("量不出抽屉进深时的保底行程。")]
    public float slideDistance = .30f;
    [Tooltip("剧情 UI 全部跳过后，再等这么久才拉开抽屉。")]
    public float delayAfterUi = .15f;
    [Tooltip("万一 UI 一直没结束，最多等这么久就强行打开。")]
    public float uiTimeout = 45f;

    GameObject fragment;
    Transform drawer;
    Transform handle;
    Vector3 openDirection = Vector3.forward;
    Vector3 fragmentOffset;
    bool placed;

    // 抽屉拉到位以后要每帧钉住的世界坐标。
    // 场景里这只抽屉上还挂着老的 FurnitureOpenable（DrawerSetup 自动装的），
    // 它会在自己的 Update 里把抽屉写回“关着”的位置 —— 实测就是“行程 0.337，实际位移 0.000”。
    // 所以这里不再借它做动画：自己插值、自己在 LateUpdate 里兜底。
    bool pulled;
    Vector3 pulledAt;

    Coroutine routine;

    /// <summary>由 BedroomToyPuzzle 在谜题解开时调用。</summary>
    public void Begin(GameObject fragmentObject)
    {
        fragment = fragmentObject;
        if (!fragment) { Debug.LogWarning("[抽屉] 没有可用的信封碎片对象。", this); return; }
        fragment.SetActive(false);
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(Sequence());
    }

    IEnumerator Sequence()
    {
        float waited = 0f;
        while (waited < uiTimeout && IsUiShowing()) { waited += Time.deltaTime; yield return null; }
        if (delayAfterUi > 0f) yield return new WaitForSeconds(delayAfterUi);

        // 剧情播完、马上要弹抽屉的这一刻，其他房间的门才允许打开
        // （在这之前一直锁着，防止玩家提前跑进别的房间、打乱顺序）。
        RoomDoorGate.Unlock();

        drawer = ResolveDrawer();
        if (!drawer)
        {
            Debug.LogWarning("[抽屉] 没找到抽屉“" + drawerHandleName + " / " + drawerMeshName + "”，碎片直接出现在原位。", this);
            fragment.SetActive(true);
            yield break;
        }

        // 这只抽屉上原本还挂着 DrawerSetup 自动装的 FurnitureOpenable。
        // 它每帧都会把抽屉写回“关着”的位置（上一版实测：行程 0.337m，实际位移 0.000m），
        // 所以这里先把它停掉并移除，动画完全由本脚本自己做。
        SilenceOpeners(drawer);

        // 方向只可能是“垂直于柜体长轴”的那一根：抽屉绝不会沿着长轴滑出去。
        Vector3 direction = ResolveOpenDirection(drawer);
        openDirection = direction;

        // 行程：让抽屉几乎整只退出柜体正面（背面还挂着三四厘米），
        // 这样从任何角度看都是“整只拉出来”，而不是在柜体里挪了一小段。
        Bounds drawerBounds = BoundsOf(drawer);
        float depth = Mathf.Abs(Vector3.Dot(drawerBounds.size, direction));
        float travel = Mathf.Clamp(depth * .95f, .28f, .42f);
        if (travel < .2f) travel = slideDistance;

        Vector3 closedAt = drawer.position;
        Transform cabinet = ResolveFurnitureRoot(drawer);
        Debug.Log("[抽屉] 目标抽屉 " + PathOf(drawer)
            + "，所属家具 " + (cabinet ? PathOf(cabinet) + " 外框 " + BoundsOf(cabinet).size.ToString("F3") : "（没找到，按单只抽屉算）")
            + "，关着时坐标 " + closedAt.ToString("F3"), this);

        // 先往判定的方向探 8cm：如果抽屉是往柜子里缩的（抽屉外壳与柜体外壳的间距变大），
        // 立刻掉头。这一小步只是探路，探完从它接着走，不会看到抽屉往回跳。
        float gapBefore = Gap(cabinet, drawerBounds, direction);
        float probe = Mathf.Min(.08f, travel * .3f);
        yield return Slide(drawer, closedAt, closedAt + direction * probe, .18f);
        if (!drawer) yield break;
        float gapAfter = Gap(cabinet, BoundsOf(drawer), direction);

        Vector3 from = closedAt + direction * probe;
        if (gapAfter > gapBefore + .02f)
        {
            direction = -direction;
            openDirection = direction;
            from = closedAt;
            if (drawer) drawer.position = closedAt;
            Debug.LogWarning("[抽屉] 方向探路发现是往柜子里推的，已掉头：" + direction.ToString("F2"), this);
        }

        // 正式拉：整只拉到位。
        yield return Slide(drawer, from, closedAt + direction * travel, Mathf.Max(.35f, pullDuration));
        if (!drawer) yield break;

        float moved = (drawer.position - closedAt).magnitude;
        float planned = (pulledAt - closedAt).magnitude;
        Debug.Log("[抽屉] 已拉开 " + PathOf(drawer)
            + "，方向 " + direction.ToString("F2")
            + "，抽屉尺寸 " + drawerBounds.size.ToString("F3")
            + "，进深 " + depth.ToString("F3") + "m"
            + "，行程 " + travel.ToString("F3")
            + "，实际位移 " + moved.ToString("F3") + "m"
            + "，目标位移 " + planned.ToString("F3") + "m"
            + "，拉开后坐标 " + drawer.position.ToString("F3"), this);
        if (planned < .12f)
            Debug.LogError("[抽屉] 抽屉几乎没动（" + planned.ToString("F3") + "m），行程算错了。", this);

        PlaceFragment();
    }

    /// <summary>
    /// 抽屉自己（以及柜体）上的开合组件会让抽屉“关不严 / 拉不开”，
    /// 这里把它们停掉：抽屉本体上的直接移除，祖先上的只禁用（不能把整件家具的交互拆了）。
    /// </summary>
    static void SilenceOpeners(Transform drawer)
    {
        foreach (var openable in drawer.GetComponents<FurnitureOpenable>())
        {
            if (!openable) continue;
            openable.enabled = false;          // Destroy 要到帧末才生效，先把 Update 停了
            Destroy(openable);
        }

        Transform current = drawer.parent;
        for (int i = 0; i < 3 && current; i++, current = current.parent)
            foreach (var openable in current.GetComponents<FurnitureOpenable>())
                if (openable) openable.enabled = false;
    }

    /// <summary>
    /// 抽屉外壳在 dir 方向上、离柜体外壳还有多远（越大越像“缩在柜子里面”）。
    /// 朝外拉会变小，往柜子里推会变大 —— 用来确认方向没判反。
    /// </summary>
    static float Gap(Transform cabinet, Bounds drawerBounds, Vector3 dir)
    {
        if (!cabinet) return 0f;
        return MaxAlong(BoundsOf(cabinet), dir) - MaxAlong(drawerBounds, dir);
    }

    /// <summary>
    /// 直线滑出。每帧同时更新 pulledAt，LateUpdate 里再兜一次底 ——
    /// 就算场景里还有别的组件（老的 FurnitureOpenable、动画）在同一帧把抽屉按回去，也不影响。
    /// </summary>
    IEnumerator Slide(Transform target, Vector3 from, Vector3 to, float duration)
    {
        pulled = true;
        pulledAt = from;
        float t = 0f;
        while (t < duration)
        {
            t += Mathf.Min(Time.deltaTime, .05f);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / Mathf.Max(.01f, duration)));
            pulledAt = Vector3.Lerp(from, to, k);
            if (!target) yield break;
            target.position = pulledAt;
            yield return null;
        }
        pulledAt = to;
        if (target) target.position = to;
    }

    void PlaceFragment()
    {
        Bounds bounds = BoundsOf(drawer);
        // 就放抽屉底板正中（不再往开口方向挪）：抽屉已经整只拉出来了，中间这个点
        // 从上方/正面都能看见；而往开口挪会让纸片在抽屉还没拉开时先跑到柜体外面，
        // 看起来就是“碎片飘在床前”。
        Vector3 target = bounds.center;
        target.y = bounds.min.y + .03f;
        // 纸片平放，只保留原有朝向。
        fragment.transform.SetPositionAndRotation(target, Quaternion.Euler(0f, fragment.transform.eulerAngles.y, 0f));

        // 碎片轴心不一定在几何中心，这里再校一次，让纸片底面刚好落在抽屉底上。
        var renderers = fragment.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length > 0)
        {
            Bounds paper = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) paper.Encapsulate(renderers[i].bounds);
            fragment.transform.position += Vector3.up * (target.y - paper.min.y);
        }

        // 纸片很薄，原来的碰撞盒贴着抽屉底，射线经常擦过去 —— 放大到至少 18cm 见方。
        EnsurePickCollider(fragment.transform);
        EnsureYellowGlow();

        fragmentOffset = fragment.transform.position - drawer.position;
        fragment.SetActive(true);
        placed = true;
        if (StoryProgress.Instance) StoryProgress.Instance.RecordClue("书桌抽屉被轻轻拉开，里面压着一张信封碎片。");
        Debug.Log("[抽屉] 信封碎片已放入抽屉：世界坐标 " + fragment.transform.position.ToString("F3"), this);
    }

    /// <summary>
    /// 给碎片套一圈暖黄色描边（只描边，不打灯）。
    /// 以前抽屉里还挂了一盏点光，纸片离得近，光会把整个抽屉内壁烧成一片黄白，
    /// 纸上的字反而看不清——所以这里只用描边告诉玩家“这里有东西”。
    /// </summary>
    void EnsureYellowGlow()
    {
        if (!fragment) return;
        var outline = fragment.GetComponent<ProximityOutline>();
        if (!outline) outline = fragment.AddComponent<ProximityOutline>();
        // Apply 会顺手把已经建好的描边材质改掉；只改字段的话颜色仍是默认的青色。
        outline.Apply(new Color(1f, .78f, .16f, .85f), .016f, 8f);   // 站在书桌前一定看得见，远了也不用亮
    }

    // 碎片自己的“E 拾取”浮字已经去掉了：游戏本体的交互提示会自己显示，
    // 这里再画一条会在屏幕上出现两条一模一样的提示（而且碎片收着的时候也会画出来，
    // 看起来像“抽屉还没拉开，碎片就已经弹出来了”）。

    static void EnsurePickCollider(Transform t)
    {
        if (!t) return;
        var box = t.GetComponent<BoxCollider>();
        if (!box) box = t.gameObject.AddComponent<BoxCollider>();
        var r = t.GetComponent<Renderer>();
        if (!r) return;

        Bounds b = r.bounds;
        Vector3 s = t.lossyScale;
        float minXZ = .18f, minY = .08f;
        box.center = t.InverseTransformPoint(b.center);
        box.size = new Vector3(
            Mathf.Max(minXZ, b.size.x) / Mathf.Max(.0001f, Mathf.Abs(s.x)),
            Mathf.Max(minY, b.size.y) / Mathf.Max(.0001f, Mathf.Abs(s.y)),
            Mathf.Max(minXZ, b.size.z) / Mathf.Max(.0001f, Mathf.Abs(s.z)));
        box.isTrigger = false;
    }

    // 抽屉拉到位以后每帧再钉一次：场景里那个老的 FurnitureOpenable 会在 Update 里
    // 把抽屉写回“关着”的位置，只靠协程赋值会被它按回去（实测位移 0.000m）。
    // LateUpdate 在所有 Update 之后执行，所以这里写进去的位置才作数。
    void LateUpdate()
    {
        if (!drawer) return;
        if (pulled) drawer.position = pulledAt;

        if (!placed || !fragment) return;
        if (!fragment.activeSelf) return;
        fragment.transform.position = drawer.position + fragmentOffset;
    }

    // ---------- 查找 ----------

    static bool IsUiShowing()
    {
        var story = StoryProgress.Instance;
        return story && story.IsShowingPage;
    }

    Transform ResolveDrawer()
    {
        Vector3 near = fragment ? fragment.transform.position : transform.position;

        // 1) 书桌模型里有三个抽屉（Draw 1/2/3_low）：剧情只弹开正中间那个。
        var drawers = FindDeskDrawersNear(near);
        if (drawers.Count > 0) return drawers[drawers.Count / 2];

        handle = FindDeep(drawerHandleName, near);
        if (handle) near = handle.position;

        // 2) 按模型节点名精确匹配（Draw 2_low），并要求尺寸像抽屉。
        var byName = FindDeep(drawerMeshName, near);
        if (byName && IsPlausibleDrawer(byName)) return byName;

        // 3) 兜底：从抽屉句柄往上找，但同样必须尺寸像抽屉，避免把整张书桌当成抽屉。
        if (handle)
        {
            var parent = handle.parent;
            for (int i = 0; i < 4 && parent; i++, parent = parent.parent)
                if (IsPlausibleDrawer(parent)) return parent;
        }
        return null;
    }

    /// <summary>
    /// 找到 near 附近这张书桌上的所有抽屉，并按“玩家站在书桌前时的从左到右”排序。
    /// 抽屉是按模型【网格名】认的（Draw 1_low / Draw 2_low / Draw 3_low）：
    /// 这些名字只在 FBX 的网格上，场景里的 GameObject 不一定叫这个名字，按物体名找会一个都找不到。
    /// 左右顺序用“抽屉自己的开合方向”推，不依赖书桌 forward 有没有转反。
    /// </summary>
    public static List<Transform> FindDeskDrawersNear(Vector3 near, float radius = 6f)
    {
        var found = new List<Transform>();
        foreach (var renderer in FindObjectsOfType<MeshRenderer>())
        {
            if (!renderer) continue;
            var filter = renderer.GetComponent<MeshFilter>();
            if (!filter || !filter.sharedMesh) continue;

            string mesh = filter.sharedMesh.name;
            if (string.IsNullOrEmpty(mesh)) continue;
            if (mesh.IndexOf("_LOD", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (!mesh.StartsWith("Draw ", System.StringComparison.OrdinalIgnoreCase)) continue;
            if (mesh.IndexOf("_low", System.StringComparison.OrdinalIgnoreCase) < 0) continue;

            var t = renderer.transform;
            if ((BoundsOf(t).center - near).sqrMagnitude > radius * radius) continue;   // 别抓到别的屋子
            if (!IsPlausibleDrawer(t)) continue;
            if (!found.Contains(t)) found.Add(t);
        }
        if (found.Count <= 1) return found;

        // 同一片区域可能有不止一张桌子：只留离 near 最近那张的抽屉。
        found.Sort((a, b) => (BoundsOf(a).center - near).sqrMagnitude.CompareTo((BoundsOf(b).center - near).sqrMagnitude));
        Transform desk = ResolveFurnitureRoot(found[0]);
        if (desk)
        {
            var same = new List<Transform>();
            foreach (var d in found) if (ResolveFurnitureRoot(d) == desk) same.Add(d);
            if (same.Count > 0) found = same;
        }

        Bounds deskBounds = desk ? BoundsOf(desk) : BoundsOf(found[0]);
        Vector3 forward = Vector3.zero;
        FurnitureOpenable opener = null;
        foreach (var d in found)
        {
            opener = d.GetComponent<FurnitureOpenable>();
            if (!opener) opener = d.GetComponentInParent<FurnitureOpenable>();
            if (opener) break;
        }
        forward = opener ? opener.PickOutwardByClearance() : Roomward(desk, deskBounds.center);
        forward = Vector3.ProjectOnPlane(forward, Vector3.up);
        if (forward.sqrMagnitude < .0004f) forward = Vector3.forward;
        forward.Normalize();
        Vector3 leftAxis = Vector3.Cross(Vector3.up, forward).normalized;   // 玩家面对书桌时的左手方向

        Vector3 center = deskBounds.center;
        found.Sort((a, b) =>
            Vector3.Dot(BoundsOf(a).center - center, leftAxis)
            .CompareTo(Vector3.Dot(BoundsOf(b).center - center, leftAxis)));

        // 正常书桌是横向排的三个抽屉；万一模型是竖着叠的（左右几乎没差），改成按高度排，
        // 这样“中间那个”仍然是玩家眼里的正中间。
        float minSide = float.MaxValue, maxSide = float.MinValue;
        foreach (var d in found)
        {
            float side = Vector3.Dot(BoundsOf(d).center - center, leftAxis);
            if (side < minSide) minSide = side;
            if (side > maxSide) maxSide = side;
        }
        if (maxSide - minSide < .3f)
            found.Sort((a, b) => BoundsOf(a).center.y.CompareTo(BoundsOf(b).center.y));
        return found;
    }

    /// <summary>书桌正面（朝房间）的方向：两侧比射线通空度，更空那侧就是房间。</summary>
    static Vector3 Roomward(Transform desk, Vector3 center)
    {
        Vector3 forward = desk ? Vector3.ProjectOnPlane(desk.forward, Vector3.up) : Vector3.forward;
        if (forward.sqrMagnitude < .0004f) forward = Vector3.forward;
        forward.Normalize();
        if (Clearance(center, -forward) > Clearance(center, forward)) forward = -forward;
        return forward;
    }

    static float Clearance(Vector3 origin, Vector3 dir)
    {
        const float max = 2.5f;
        var hits = Physics.RaycastAll(origin + dir * .05f, dir, max, ~0, QueryTriggerInteraction.Ignore);
        float best = max;
        foreach (var h in hits)
            if (h.collider && h.distance > .02f && h.distance < best) best = h.distance;
        return best;
    }

    // 抽屉应该是“一小块”家具：有渲染器、包围盒明显小于整件家具。
    static bool IsPlausibleDrawer(Transform t)
    {
        var renderers = t.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0 || renderers.Length > 8) return false;
        float magnitude = BoundsOf(t).size.magnitude;
        return magnitude > .02f && magnitude < 1f;
    }

    /// <summary>
    /// 抽屉该往哪一面拉出去。
    ///
    /// 只认一条物理事实：**抽屉绝不会沿着柜体的长轴滑出去**。
    /// 床下柜是 1.44m 长、0.62m 深，三个抽屉沿长轴并排，进深方向只有垂直于长轴的那一根轴。
    /// 以前按“四面里哪面最空”选，柜体端头外面也是空的（1.2m 全通），长轴就会被选进来，
    /// 抽屉于是横着滑出去 —— 就是玩家看到的“左右推”。
    ///
    /// 现在：先取柜体长轴，再取它的水平垂线当进深轴；正负用“哪一侧抽屉面板与柜体正面齐平”
    /// 定（抽屉面板一定与柜体正面齐平，背面缩在柜体里 0.24m）。
    /// </summary>
    Vector3 ResolveOpenDirection(Transform d)
    {
        Bounds drawerBounds = BoundsOf(d);
        Transform furniture = ResolveFurnitureRoot(d);
        Bounds cabinet = furniture ? BoundsOf(furniture) : drawerBounds;

        // 柜体长轴 = 水平方向上更长的那条边；它的垂线才是抽屉的进深方向。
        Vector3 longAxis = cabinet.size.x >= cabinet.size.z ? Vector3.right : Vector3.forward;
        Vector3 axis = Vector3.Cross(Vector3.up, longAxis).normalized;

        Vector3 dir;
        float frontGap, backGap;
        if (TryPickSide(cabinet, drawerBounds, axis, out dir, out frontGap, out backGap))
        {
            Debug.Log("[抽屉] " + PathOf(d) + " 柜体长轴 " + AxisName(longAxis)
                + "，进深轴 " + AxisName(axis)
                + "；面板间距 正 " + frontGap.ToString("F3") + " / 背 " + backGap.ToString("F3")
                + " -> 拉出方向 " + AxisName(dir), this);
            return dir;
        }

        // 齐平关系读不出来（整只抽屉被床架包住等）：退到进深轴两侧的通空度，
        // 只在两根轴上比，长轴已经被排除，不可能再横向滑。
        float pos = FaceClearance(drawerBounds, axis, d);
        float neg = FaceClearance(drawerBounds, -axis, d);
        Vector3 byClearance = pos >= neg ? axis : -axis;
        Debug.LogWarning("[抽屉] 面板间距不可信（正 " + frontGap.ToString("F3") + " / 背 "
            + backGap.ToString("F3") + "），改用进深轴通空度 +" + pos.ToString("F2")
            + " / -" + neg.ToString("F2") + " -> " + AxisName(byClearance), this);
        return byClearance;
    }

    /// <summary>抽屉正面是否已经跑到柜体正面之外（= 真的拉出来了）。</summary>
    static bool GotOutside(Transform drawer, Vector3 dir)
    {
        Transform furniture = ResolveFurnitureRoot(drawer);
        if (!furniture) return true;
        return MaxAlong(BoundsOf(drawer), dir) - MaxAlong(BoundsOf(furniture), dir) > .12f;
    }

    /// <summary>
    /// 抽屉某个侧面朝外有多“空”：在该面外侧铺 3×3 个采样点各打一条射线，取最小距离。
    /// 返回 max 表示这一面完全通向房间。
    /// </summary>
    static float FaceClearance(Bounds b, Vector3 dir, Transform self)
    {
        const float max = 1.2f;
        Vector3 face = b.center + dir * Mathf.Abs(Vector3.Dot(b.extents, dir));
        Vector3 up = Vector3.up;
        Vector3 side = Vector3.Cross(up, dir).normalized;
        float halfUp = Mathf.Abs(Vector3.Dot(b.extents, up)) * .55f;
        float halfSide = Mathf.Abs(Vector3.Dot(b.extents, side)) * .55f;

        float min = max;
        for (int i = -1; i <= 1; i++)
        {
            for (int j = -1; j <= 1; j++)
            {
                Vector3 origin = face + up * (halfUp * i) + side * (halfSide * j) + dir * .05f;
                float blocked = max;
                foreach (var hit in Physics.RaycastAll(origin, dir, max, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (!hit.collider) continue;
                    var t = hit.collider.transform;
                    if (t == self || t.IsChildOf(self)) continue;        // 抽屉自己不算
                    if (hit.distance > .02f && hit.distance < blocked) blocked = hit.distance;
                }
                // 射线从碰撞体内部出发时打不到那个碰撞体（Unity 不打“出射”），
                // 所以再在面前 12cm 处探一次体积：有别的碰撞体压在那儿，就当这面被挡住。
                if (blocked >= max && BlockedByVolume(origin + dir * .07f, self)) blocked = .05f;
                if (blocked < min) min = blocked;
            }
        }
        return min;
    }

    static readonly Collider[] overlapBuffer = new Collider[8];

    static bool BlockedByVolume(Vector3 point, Transform self)
    {
        int count = Physics.OverlapBoxNonAlloc(point, Vector3.one * .06f, overlapBuffer,
            Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var c = overlapBuffer[i];
            if (!c) continue;
            var t = c.transform;
            if (t == self || t.IsChildOf(self)) continue;
            return true;
        }
        return false;
    }

    static string AxisName(Vector3 d)
    {
        if (Mathf.Abs(d.x) > .5f) return d.x > 0f ? "+X" : "-X";
        return d.z > 0f ? "+Z" : "-Z";
    }

    /// <summary>兜底：按“哪侧与家具外框齐平”判正面（书桌这类进深和柜体接近的家具仍然好用）。</summary>
    Vector3 ResolveOpenDirectionGeometric(Transform d, Bounds drawerBounds)
    {
        Transform furniture = ResolveFurnitureRoot(d);

        if (furniture)
        {
            Bounds furnitureBounds = BoundsOf(furniture);
            var axes = new List<Vector3>(6);
            // 句柄上配过滑动轴的（FurnitureOpenable）优先，只取“轴”，正负由几何决定。
            var configured = handle ? handle.GetComponent<FurnitureOpenable>() : null;
            if (configured) AddAxis(axes, configured.slideDirection);
            AddAxis(axes, furniture.right);
            AddAxis(axes, furniture.forward);
            AddAxis(axes, Vector3.right);
            AddAxis(axes, Vector3.forward);

            Vector3 best = Vector3.zero;
            float bestGap = float.MaxValue;
            float bestBack = 0f;
            foreach (var axis in axes)
            {
                Vector3 dir;
                float frontGap, backGap;
                bool credible = TryPickSide(furnitureBounds, drawerBounds, axis, out dir, out frontGap, out backGap);
                if (credible)
                {
                    best = dir; bestGap = frontGap; bestBack = backGap;
                    break;                       // 按优先级取第一个可信的轴
                }
                if (frontGap < bestGap) { best = dir; bestGap = frontGap; bestBack = backGap; }
            }

            Debug.Log("[抽屉] 家具 " + PathOf(furniture) + "，外框 " + furnitureBounds.size.ToString("F3")
                + "；面板间距 正 " + bestGap.ToString("F3") + " / 背 " + bestBack.ToString("F3")
                + "，候选方向 " + best.ToString("F2"), this);

            // 正面间距小、背面间距大，才算可信。
            // 注意：这里只定“沿哪根轴”和正负，不再叠加通空度校正 ——
            // FurnitureOpenable.PickOutwardByClearance 的选轴逻辑（PanelAxis）用的也是
            // “两侧通空度差”，在床下柜上同样会把长轴当成进深轴，叠在这里只会互相打架。
            if (best.sqrMagnitude > .5f && bestGap < .12f && bestBack > bestGap + .03f)
                return best;
        }

        // 兜底：沿用句柄上已配置的滑动轴（只取轴不算符号，符号朝玩家）。
        var fallback = handle ? handle.GetComponent<FurnitureOpenable>() : null;
        if (fallback)
        {
            Vector3 axis = Vector3.ProjectOnPlane(fallback.slideDirection, Vector3.up);
            if (axis.sqrMagnitude > .01f)
            {
                axis.Normalize();
                var player = FindObjectOfType<PlayerController>();
                if (player)
                {
                    Vector3 toPlayer = Vector3.ProjectOnPlane(player.transform.position - drawerBounds.center, Vector3.up);
                    if (Vector3.Dot(axis, toPlayer) < 0f) axis = -axis;
                }
                Debug.LogWarning("[抽屉] 面板间距不可信，改用已配置的滑动轴 " + axis.ToString("F2"), this);
                return axis;
            }
        }

        Vector3 forward = Vector3.ProjectOnPlane(d.forward, Vector3.up);
        return forward.sqrMagnitude > .0004f ? forward.normalized : Vector3.forward;
    }

    // 沿 axis 的两个方向分别比较“家具外表面 - 抽屉外表面”的间距，间距小的一侧就是抽屉面板朝向。
    // 返回是否可信：正面几乎齐平（< 12 cm），且背面明显更深。
    static bool TryPickSide(Bounds furnitureBounds, Bounds drawerBounds, Vector3 axis, out Vector3 dir, out float frontGap, out float backGap)
    {
        float gapPositive = MaxAlong(furnitureBounds, axis) - MaxAlong(drawerBounds, axis);
        float gapNegative = MaxAlong(furnitureBounds, -axis) - MaxAlong(drawerBounds, -axis);
        if (gapNegative < gapPositive)
        {
            dir = -axis;
            frontGap = gapNegative;
            backGap = gapPositive;
        }
        else
        {
            dir = axis;
            frontGap = gapPositive;
            backGap = gapNegative;
        }
        return frontGap < .12f && backGap > frontGap + .03f;
    }

    // 找到“整件家具”那一层：抽屉本身约 0.4 m，再往上第一层明显变大（书桌约 1.7 m）就是它。
    // 之后只接受尺寸几乎不变的包装节点（空父级），避免把整间房的包围盒当成家具。
    static Transform ResolveFurnitureRoot(Transform d)
    {
        Transform found = null;
        float foundSize = 0f;
        for (Transform t = d.parent; t; t = t.parent)
        {
            float size = BoundsOf(t).size.magnitude;
            if (size > 3f) break;                       // 再往上就是一整间房了
            if (size <= 1.2f) continue;                 // 还是抽屉/局部零件
            if (!found) { found = t; foundSize = size; continue; }
            if (size > foundSize * 1.05f) break;        // 已经膨胀，说明混进了别的家具
            found = t;
            foundSize = size;
        }
        return found;
    }

    static void AddAxis(List<Vector3> axes, Vector3 raw)
    {
        Vector3 axis = Vector3.ProjectOnPlane(raw, Vector3.up);
        if (axis.sqrMagnitude < .04f) return;     // 竖直轴，跳过
        axis.Normalize();
        foreach (var a in axes)
            if (Mathf.Abs(Vector3.Dot(a, axis)) > .999f) return;   // 和已有轴重复（含反向）
        axes.Add(axis);
    }

    // 包围盒在 dir 方向上的最外表面坐标。
    static float MaxAlong(Bounds b, Vector3 dir)
    {
        return Vector3.Dot(b.center, dir)
            + .5f * (Mathf.Abs(dir.x) * b.size.x + Mathf.Abs(dir.y) * b.size.y + Mathf.Abs(dir.z) * b.size.z);
    }

    static Bounds BoundsOf(Transform t)
    {
        var renderers = t.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(t.position, Vector3.one * .2f);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    static void DisableManualToggle(Transform t)
    {
        if (!t) return;
        Transform current = t;
        for (int i = 0; i < 3 && current; i++)
        {
            var openable = current.GetComponent<FurnitureOpenable>();
            if (openable) openable.enabled = false;
            current = current.parent;
        }
    }

    // 在当前场景（包含未激活物体）里按名字找，多个同名时取离 near 最近的。
    public static Transform FindDeep(string name)
    {
        return FindDeep(name, Vector3.zero);
    }

    public static Transform FindDeep(string name, Vector3 near)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid()) return null;

        Transform best = null;
        float bestDistance = float.MaxValue;
        bool bestExact = false;
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                bool exact = t.name == name;
                if (!exact && !t.name.StartsWith(name, System.StringComparison.OrdinalIgnoreCase)) continue;
                float distance = (t.position - near).sqrMagnitude;
                if (exact && !bestExact) { best = t; bestDistance = distance; bestExact = true; }
                else if (exact == bestExact && distance < bestDistance) { best = t; bestDistance = distance; }
            }
        }
        return best;
    }

    static string PathOf(Transform t)
    {
        var parts = new List<string>();
        while (t) { parts.Add(t.name); t = t.parent; }
        parts.Reverse();
        return string.Join("/", parts.ToArray());
    }
}
