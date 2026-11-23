using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 第三关（橙色密码箱）的运行时装配：不改场景文件，进关时自动给
/// SM_Box_A15 挂上密码箱逻辑与描边、给 stool 挂上推动与踩踏、给日历挂上可阅读线索。
/// 场景里找不到密码箱就什么都不做，不会影响其他关卡。
/// </summary>
public static class PasswordPuzzleSetup
{
    const string BoxName = "SM_Box_A15";
    const string StoolName = "stool";
    const string CalendarPrefix = "日历";

    /// <summary>凳子碰撞盒底面抬离地面的高度：门框底部有一道两三厘米的门槛，抬起来才推得过去。</summary>
    const float FootLift = .06f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Setup()
    {
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name != "Level 1") return;
        Transform box = Find(BoxName);
        if (!box) return;                       // 不是第三关的场景，直接跳过

        EnsureCollider(box.gameObject, true);

        if (!box.GetComponent<PasswordBoxPuzzle>())
        {
            var outline = box.GetComponent<ProximityOutline>();
            if (!outline) outline = box.gameObject.AddComponent<ProximityOutline>();
            outline.revealDistance = 3.2f;
            outline.outlineWidth = .005f;
            outline.outlineColor = new Color(1f, .62f, .22f, .95f);
            box.gameObject.AddComponent<PasswordBoxPuzzle>();
        }

        Transform stool = Find(StoolName);
        if (stool && !stool.GetComponent<StoolStep>())
        {
            // 凳子必须用“实心”碰撞体（盒/球/胶囊）。模型自带的非凸网格碰撞体不能做扫掠，
            // PushProp 每帧拿不到地面信息，凳子会直接漏到地板下面消失。
            MakeSolidCollider(stool.gameObject);
            NudgeOutOfFurniture(stool);
            SnapToFloor(stool);
            if (!stool.GetComponent<PushProp>()) stool.gameObject.AddComponent<PushProp>();
            // 碰撞盒底面比网格底面高一点，凳子才能跨过门框底部的门槛；
            // PushProp 的贴地检测会补偿这个值，网格仍然贴着地面。
            stool.GetComponent<PushProp>().footLift = FootLift;
            stool.gameObject.AddComponent<StoolStep>();
            LogStool(stool);
        }

        int calendars = 0;
        foreach (var t in AllTransforms())
        {
            if (!t.name.StartsWith(CalendarPrefix)) continue;
            if (t.GetComponent<ReadableClue>()) { calendars++; continue; }
            EnsureCollider(t.gameObject, true);
            var clue = t.gameObject.AddComponent<ReadableClue>();
            clue.title = "日历";
            clue.description = "2022年10月的日历，5号被红笔圈了出来。";
            clue.glowRange = 2.2f;
            clue.emissive = false;
            calendars++;
        }

        Debug.Log("[第三关] 密码箱装配完成：箱子 " + box.name
            + "，凳子 " + (stool ? stool.name : "未找到")
            + "，日历 " + calendars + " 个。", box);
    }

    static Transform Find(string name)
    {
        foreach (var t in AllTransforms())
            if (t.name == name) return t;
        return null;
    }

    static List<Transform> AllTransforms()
    {
        var list = new List<Transform>();
        var scene = SceneManager.GetActiveScene();
        if (!scene.IsValid()) return list;
        foreach (var root in scene.GetRootGameObjects())
            list.AddRange(root.GetComponentsInChildren<Transform>(true));
        return list;
    }

    /// <summary>
    /// 给凳子换成盒碰撞体：包围盒取所有渲染器的世界包围盒。
    /// 非凸网格碰撞体无法参与 Rigidbody 扫掠/推动，是凳子“漏下去”的根源。
    /// </summary>
    static void MakeSolidCollider(GameObject target)
    {
        foreach (var c in target.GetComponentsInChildren<Collider>(true))
            if (c is BoxCollider || c is SphereCollider || c is CapsuleCollider) return;

        foreach (var c in target.GetComponentsInChildren<Collider>(true))
            if (c is MeshCollider) Object.Destroy(c);

        Bounds world;
        if (!BoundsOf(target, out world)) return;

        // 横向收 12%：凳子的坐垫比门框还宽一点，收窄后才能顺门推过去。
        // 竖向：高度从底部收掉 FootLift，让碰撞盒底面离地，凳子就能蹭过门槛（网格不动，外观不变）。
        // 注意凳子实例带着 -90° 的 X 旋转，所以尺寸/位移都在世界空间算完，再换算回本地。
        Vector3 worldExtents = world.extents;
        worldExtents.x *= .88f;
        worldExtents.z *= .88f;
        worldExtents.y -= FootLift * .5f;
        Vector3 worldCenter = world.center + Vector3.up * (FootLift * .5f);

        var box = target.AddComponent<BoxCollider>();
        box.center = target.transform.InverseTransformPoint(worldCenter);
        Vector3 half = target.transform.InverseTransformVector(worldExtents);
        box.size = new Vector3(Mathf.Abs(half.x), Mathf.Abs(half.y), Mathf.Abs(half.z)) * 2f;
    }

    /// <summary>
    /// 凳子如果和同体积的家具（比如同一位置还放了一把椅子）几乎完全重合，会被挡在里面看不见、
    /// 也推不动。这里把它平移到旁边一块空地上；找不到空地就保持原样。
    /// </summary>
    static void NudgeOutOfFurniture(Transform stool)
    {
        Bounds world;
        if (!BoundsOf(stool.gameObject, out world)) return;
        float stoolSize = world.size.magnitude;
        float reach = Mathf.Max(.25f, world.extents.magnitude * .5f);

        Collider blocker = null;
        foreach (var other in Physics.OverlapBox(world.center, world.extents * .9f, Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (!other || other.transform.IsChildOf(stool)) continue;
            if (other.bounds.size.magnitude > stoolSize * 3f) continue;          // 地板、墙这类大件不算
            if (Vector3.Distance(other.bounds.center, world.center) > reach) continue;
            blocker = other;
            break;
        }
        if (!blocker) return;

        Vector3 away = world.center - blocker.bounds.center;
        away.y = 0f;
        if (away.sqrMagnitude < .0004f) away = stool.forward;
        away = Vector3.ProjectOnPlane(away, Vector3.up).normalized;
        if (away.sqrMagnitude < .5f) away = Vector3.forward;

        float step = world.extents.magnitude + .35f;
        for (int i = 0; i < 4; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, i * 90f, 0f) * away;
            if (!FreeAt(world.center + dir * step, world.extents * .9f, stool)) continue;
            stool.position += dir * step;
            Debug.Log("[第三关] 凳子和「" + blocker.name + "」重叠，已挪到旁边的空地。", stool);
            return;
        }
        Debug.LogWarning("[第三关] 凳子被「" + blocker.name + "」挡住，四周没有空地，保持原位。", stool);
    }

    static bool FreeAt(Vector3 center, Vector3 extents, Transform stool)
    {
        foreach (var other in Physics.OverlapBox(center, extents, Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (!other || other.transform.IsChildOf(stool)) continue;
            return false;
        }
        return true;
    }

    /// <summary>凳子悬空或陷进地板时，按包围盒把它贴到脚下的地面上。</summary>
    static void SnapToFloor(Transform stool)
    {
        Bounds world;
        if (!BoundsOf(stool.gameObject, out world)) return;

        Vector3 from = new Vector3(world.center.x, world.min.y + .05f, world.center.z);
        foreach (var hit in Physics.RaycastAll(from, Vector3.down, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(stool)) continue;
            if (hit.normal.y < .3f) continue;
            float delta = hit.point.y - world.min.y;
            if (Mathf.Abs(delta) < .02f || Mathf.Abs(delta) > 1.5f) return;
            stool.position += Vector3.up * delta;
            return;
        }
    }

    static bool BoundsOf(GameObject target, out Bounds bounds)
    {
        bounds = new Bounds(target.transform.position, Vector3.zero);
        bool any = false;
        foreach (var r in target.GetComponentsInChildren<Renderer>(true))
        {
            if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return any;
    }

    static void LogStool(Transform stool)
    {
        Bounds world;
        bool has = BoundsOf(stool.gameObject, out world);
        var kinds = new List<string>();
        foreach (var c in stool.GetComponentsInChildren<Collider>(true)) kinds.Add(c.GetType().Name);

        // 顺便看看它是不是被别的家具“埋”住了（同位置放了椅子之类），方便定位看不见的原因。
        int overlaps = 0;
        if (has)
        {
            foreach (var other in Physics.OverlapBox(world.center, world.extents * .8f, Quaternion.identity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                if (!other || other.transform.IsChildOf(stool)) continue;
                overlaps++;
            }
        }

        Debug.Log("[第三关] 凳子 " + stool.name + " 位置 " + stool.position.ToString("F2")
            + "，包围盒 " + (has ? world.size.ToString("F2") : "无渲染器")
            + "，碰撞体 " + string.Join("/", kinds.ToArray())
            + "，与 " + overlaps + " 个物体重叠", stool);
    }

    /// <summary>没有碰撞体时按渲染网格补一个，保证射线能打到、玩家能站上去。</summary>
    static void EnsureCollider(GameObject target, bool useBox)
    {
        if (target.GetComponentInChildren<Collider>()) return;

        if (!useBox)
        {
            var filter = target.GetComponentInChildren<MeshFilter>(true);
            if (filter && filter.sharedMesh)
            {
                var mesh = filter.gameObject.AddComponent<MeshCollider>();
                mesh.sharedMesh = filter.sharedMesh;
                mesh.convex = false;
                return;
            }
        }

        var renderer = target.GetComponentInChildren<Renderer>(true);
        if (!renderer) return;
        Bounds world = renderer.bounds;
        var box = target.AddComponent<BoxCollider>();
        box.center = target.transform.InverseTransformPoint(world.center);
        Vector3 scale = target.transform.lossyScale;
        box.size = new Vector3(
            Mathf.Max(.05f, world.size.x / Mathf.Max(.0001f, Mathf.Abs(scale.x))),
            Mathf.Max(.05f, world.size.y / Mathf.Max(.0001f, Mathf.Abs(scale.y))),
            Mathf.Max(.05f, world.size.z / Mathf.Max(.0001f, Mathf.Abs(scale.z))));
    }
}
