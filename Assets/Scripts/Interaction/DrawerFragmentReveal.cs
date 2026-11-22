using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// 关卡一收尾：四个玩偶归位、剧情 UI 播完之后，
// 自动拉开书桌抽屉（Draw 1_low，沿面板法线垂直于桌体滑出），再把信封碎片①（对象007）放进抽屉里，供玩家拾取。
[DisallowMultipleComponent]
public sealed class DrawerFragmentReveal : MonoBehaviour
{
    [Header("抽屉定位")]
    [Tooltip("场景里作为抽屉句柄的空物体名字。")]
    public string drawerHandleName = "书桌抽屉 1";
    [Tooltip("抽屉模型节点名（来自家具模型），优先按这个名字找。")]
    public string drawerMeshName = "Draw 1_low";

    [Header("开启动画")]
    public float slideDistance = .26f;
    public float slideDuration = .55f;
    [Tooltip("剧情 UI 全部跳过后，再等这么久才拉开抽屉。")]
    public float delayAfterUi = .15f;
    [Tooltip("万一 UI 一直没结束，最多等这么久就强行打开。")]
    public float uiTimeout = 45f;

    GameObject fragment;
    Transform drawer;
    Transform handle;
    Vector3 fragmentOffset;
    bool placed;
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

        drawer = ResolveDrawer();
        if (!drawer)
        {
            Debug.LogWarning("[抽屉] 没找到抽屉“" + drawerHandleName + " / " + drawerMeshName + "”，碎片直接出现在原位。", this);
            fragment.SetActive(true);
            yield break;
        }

        // 统一交给 FurnitureOpenable 处理：它用“射线测通空 + 朝玩家”判定方向，
        // 保证剧情打开和玩家手动按 E 走的是同一套逻辑，不会再横向滑出去。
        var openable = drawer.GetComponent<FurnitureOpenable>();
        if (!openable && handle) openable = handle.GetComponent<FurnitureOpenable>();

        if (openable)
        {
            DisableManualToggle(drawer);
            openable.enabled = true;
            openable.slide = true;
            openable.speed = .9f;                     // 慢慢拉开，给玩家反应时间
            openable.Open();

            float guard = 0f;
            while (guard < 4f && openable && openable.OpenAmount < .995f)
            {
                guard += Time.deltaTime;
                yield return null;
            }
            if (!drawer) yield break;
            Debug.Log("[抽屉] 已按统一算法拉开 " + PathOf(drawer)
                + "，方向 " + openable.OpeningDirection.ToString("F2"), this);
        }
        else
        {
            // 没有 FurnitureOpenable 时的老路径（保底）
            DisableManualToggle(drawer);
            if (handle && handle != drawer) DisableManualToggle(handle);

            Vector3 closed = drawer.position;
            Vector3 direction = ResolveOpenDirection(drawer);
            Vector3 open = closed + direction * slideDistance;
            Debug.Log("[抽屉] 拉开 " + PathOf(drawer) + "，方向 " + direction.ToString("F2"), this);

            float t = 0f;
            while (t < slideDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / Mathf.Max(.01f, slideDuration)));
                if (!drawer) yield break;
                drawer.position = Vector3.Lerp(closed, open, k);
                yield return null;
            }
            if (!drawer) yield break;
            drawer.position = open;
        }

        PlaceFragment();
    }

    void PlaceFragment()
    {
        Bounds bounds = BoundsOf(drawer);
        Vector3 target = new Vector3(bounds.center.x, bounds.min.y + .02f, bounds.center.z);
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

        fragmentOffset = fragment.transform.position - drawer.position;
        fragment.SetActive(true);
        placed = true;
        if (StoryProgress.Instance) StoryProgress.Instance.RecordClue("书桌抽屉被轻轻拉开，里面压着一张信封碎片。");
        Debug.Log("[抽屉] 信封碎片已放入抽屉：世界坐标 " + fragment.transform.position.ToString("F3"), this);
    }

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

    // 抽屉还能被关回去时，碎片跟着一起动，不会浮在半空。
    void LateUpdate()
    {
        if (!placed || !drawer || !fragment || !fragment.activeSelf) return;
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
        handle = FindDeep(drawerHandleName, near);
        if (handle) near = handle.position;

        // 1) 按模型节点名精确匹配（Draw 1_low），并要求尺寸像抽屉。
        var byName = FindDeep(drawerMeshName, near);
        if (byName && IsPlausibleDrawer(byName)) return byName;

        // 2) 兜底：从抽屉句柄往上找，但同样必须尺寸像抽屉，避免把整张书桌当成抽屉。
        if (handle)
        {
            var parent = handle.parent;
            for (int i = 0; i < 4 && parent; i++, parent = parent.parent)
                if (IsPlausibleDrawer(parent)) return parent;
        }
        return null;
    }

    // 抽屉应该是“一小块”家具：有渲染器、包围盒明显小于整件家具。
    static bool IsPlausibleDrawer(Transform t)
    {
        var renderers = t.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0 || renderers.Length > 8) return false;
        float magnitude = BoundsOf(t).size.magnitude;
        return magnitude > .02f && magnitude < 1f;
    }

    // 抽屉只能沿“垂直于面板”的方向抽出。面板几乎与家具外框齐平，而抽屉的背面离外框很远，
    // 所以：在几个候选水平轴里，比较“桌体外表面 - 抽屉外表面”的间距，间距最小的那一侧就是正面。
    Vector3 ResolveOpenDirection(Transform d)
    {
        Bounds drawerBounds = BoundsOf(d);
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
            if (best.sqrMagnitude > .5f && bestGap < .12f && bestBack > bestGap + .03f) return best;
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
