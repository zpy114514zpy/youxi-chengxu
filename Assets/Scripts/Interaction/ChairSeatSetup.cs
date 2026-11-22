using UnityEngine;

/// <summary>
/// 给卧室书桌前那把椅子装上 E 坐下 / Q+WASD 推动。
/// 和这套工程里其它东西一样，全部在运行时装配，不改场景文件。
/// </summary>
public static class ChairSeatSetup
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Setup()
    {
        if (!GameObject.Find("BP_Door_Inside_A5")) return;      // 只在第一关装

        var chair = FindDeskChair();
        if (!chair)
        {
            Debug.Log("[椅子] 没在书桌前找到椅子，跳过。");
            return;
        }

        EnsurePushable(chair);
        if (!chair.GetComponent<ChairSeat>()) chair.gameObject.AddComponent<ChairSeat>();
        Debug.Log("[椅子] 书桌前的椅子已可交互：" + chair.name + " @ " + chair.position.ToString("F2"), chair);
    }

    /// <summary>书桌前那把椅子：场景里本来就叫 old_chair；找不到就退回“离书桌抽屉最近的一把椅子”。</summary>
    static Transform FindDeskChair()
    {
        var named = GameObject.Find("old_chair");
        if (named && named.GetComponentInChildren<Renderer>()) return named.transform;

        // 先收集，再打分：一趟扫下来会遇到“椅子先于书桌”的顺序问题。
        var chairs = new System.Collections.Generic.List<Transform>();
        var drawers = new System.Collections.Generic.List<Vector3>();

        foreach (var renderer in Object.FindObjectsOfType<MeshRenderer>())
        {
            if (!renderer) continue;
            var filter = renderer.GetComponent<MeshFilter>();
            if (!filter || !filter.sharedMesh) continue;
            string mesh = filter.sharedMesh.name;
            if (string.IsNullOrEmpty(mesh)) continue;

            if (mesh.StartsWith("Draw ") && mesh.IndexOf("_low", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                drawers.Add(renderer.bounds.center);
                continue;
            }

            bool chair = mesh.StartsWith("SM_Chair", System.StringComparison.OrdinalIgnoreCase)
                      || mesh.StartsWith("SM_OfficeChair", System.StringComparison.OrdinalIgnoreCase)
                      || mesh.StartsWith("SM_RollingChair", System.StringComparison.OrdinalIgnoreCase);
            if (chair && !chairs.Contains(renderer.transform)) chairs.Add(renderer.transform);
        }
        if (chairs.Count == 0 || drawers.Count == 0) return null;

        Transform best = null;
        float bestScore = float.MaxValue;
        foreach (var chair in chairs)
        {
            // 只用“离书桌最近的那把”，免得把客厅、别的房间的椅子也装上。
            float score = float.MaxValue;
            Vector3 at = BoundsOf(chair).center;
            foreach (var drawer in drawers)
                score = Mathf.Min(score, (at - drawer).sqrMagnitude);

            if (score >= bestScore) continue;
            bestScore = score;
            best = chair;
        }
        return best;
    }

    /// <summary>补一个运动学刚体和外包碰撞盒，让它能被 Q+WASD 推着走。</summary>
    static void EnsurePushable(Transform chair)
    {
        var body = chair.GetComponent<Rigidbody>();
        if (!body) body = chair.gameObject.AddComponent<Rigidbody>();
        body.isKinematic = true;
        body.useGravity = false;
        // 推的时候只让它平移，别翻倒。
        body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        // 推挤用的是 Rigidbody.SweepTest，只认凸碰撞体；椅子模型自带的是凹的 MeshCollider，
        // 所以额外补一个外包盒专门给它扫。凹的那个留着 —— 量椅面、挡人还得靠它。
        if (!chair.GetComponent<BoxCollider>())
        {
            var box = chair.gameObject.AddComponent<BoxCollider>();
            Bounds bounds = BoundsOf(chair);
            Vector3 scale = chair.lossyScale;
            box.center = chair.InverseTransformPoint(bounds.center);
            box.size = new Vector3(
                bounds.size.x / Mathf.Max(.0001f, Mathf.Abs(scale.x)),
                bounds.size.y / Mathf.Max(.0001f, Mathf.Abs(scale.y)),
                bounds.size.z / Mathf.Max(.0001f, Mathf.Abs(scale.z)));
        }

        if (!chair.GetComponent<PushProp>()) chair.gameObject.AddComponent<PushProp>();
    }

    static Bounds BoundsOf(Transform target)
    {
        var renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(target.position, Vector3.one * .4f);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            if (renderers[i]) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }
}
