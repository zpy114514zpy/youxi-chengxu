using UnityEngine;

/// <summary>
/// 第二关的灯具开关：和第一关一样，走到灯前、准星对准，提示「E 开灯 / E 关灯」，按 E 切换。
///
/// 第二关的场景是从 Unreal 导进来的，灯只有 Light 组件，没有第一关那套 RoomLamp / HouseLightSwitch。
/// 这里在运行时把 RoomLamp 补到灯上（负责真的关掉光源），交互本身走 L2Interactable，
/// 由 PlayerInventory 统一分发和显示提示 —— 所以提示条的位置、字体和「E 拾取」完全一致。
/// 咔哒声也用第一关那段 GameAudio.PlayLamp。
/// </summary>
public sealed class Level2LampSwitch : L2Interactable
{
    RoomLamp lamp;

    public override string Prompt => lamp == null ? "" : (lamp.isOn ? "E 关灯" : "E 开灯");

    public override void Interact(PlayerInventory inventory)
    {
        if (!lamp) return;
        bool next = !lamp.isOn;
        lamp.SetOn(next);
        // 开灯音高略高、关灯略低，和第一关一致。
        GameAudio.PlayLamp(next ? 1.04f : .96f);
        if (next) Debug.Log("[第二关] 开灯 " + name);
        else Debug.Log("[第二关] 关灯 " + name);
    }

    /// <summary>
    /// 给一盏灯装上开关。装过就直接返回，重复调用安全。
    /// </summary>
    public static Level2LampSwitch AttachTo(Light source)
    {
        if (!source) return null;
        var existing = source.GetComponent<Level2LampSwitch>();
        if (existing) return existing;

        var roomLamp = EnsureRoomLamp(source);

        // 先按灯模型的渲染包围盒做一个“略大于灯罩”的交互盒。
        // 台灯的 Light 点常常缩在灯罩里面，原来那个 0.3m 的小盒子被灯罩包着，
        // 玩家准星对着灯罩扫过去经常什么都打不到 —— 这是这间卧室两台灯按不出提示的原因。
        if (TryModelBounds(source.transform, out var b))
        {
            var probe = new GameObject("开关·交互");
            probe.transform.position = b.center;
            var box = probe.AddComponent<BoxCollider>();
            box.size = new Vector3(
                Mathf.Max(.30f, b.size.x * .92f),
                Mathf.Max(.30f, b.size.y * .92f),
                Mathf.Max(.30f, b.size.z * .92f));

            var sw = probe.AddComponent<Level2LampSwitch>();
            sw.lamp = roomLamp;
            Debug.Log($"[第二关] 灯具交互盒 {source.name} 位置 {L2.Fmt(b.center)} 尺寸 {L2.Fmt(box.size)}");
            return sw;
        }

        // 找不到灯模型（补光用的空 Light）时退回原做法：灯上直接补一个 0.3m 的盒子。
        var col = source.GetComponent<Collider>();
        if (!col || col.isTrigger)     // 灯上只有 trigger（Unreal 的灯常常带一个触发球）时，球扫也会跳过它
        {
            var b2 = source.gameObject.AddComponent<BoxCollider>();
            // 灯模型常常带 100 倍的导入缩放，尺寸要按世界尺寸换算，否则一个小盒子会变成 30 米。
            var s = source.transform.lossyScale;
            b2.size = new Vector3(
                .30f / Mathf.Max(.0001f, Mathf.Abs(s.x)),
                .30f / Mathf.Max(.0001f, Mathf.Abs(s.y)),
                .30f / Mathf.Max(.0001f, Mathf.Abs(s.z)));
        }

        var fallback = source.gameObject.AddComponent<Level2LampSwitch>();
        fallback.lamp = roomLamp;
        return fallback;
    }

    /// <summary>
    /// 墙上的开关面板（Unreal 场景里这些白面板是 Outlet）也能开灯：
    /// 面板本身没有碰撞体，给它补一个小盒子，交互指向同一盏灯的 RoomLamp —— 和灯上的开关共享状态。
    /// </summary>
    public static Level2LampSwitch AttachPanel(GameObject host, Light source, float worldSize)
    {
        if (!host || !source) return null;
        var existing = host.GetComponent<Level2LampSwitch>();
        if (existing) return existing;

        var roomLamp = EnsureRoomLamp(source);

        var col = host.GetComponent<Collider>();
        if (!col || col.isTrigger)
        {
            var box = host.AddComponent<BoxCollider>();
            // 面板模型也常带 100 倍导入缩放，尺寸要按世界尺寸换算回本地。
            var s = host.transform.lossyScale;
            box.size = new Vector3(
                worldSize / Mathf.Max(.0001f, Mathf.Abs(s.x)),
                worldSize / Mathf.Max(.0001f, Mathf.Abs(s.y)),
                worldSize / Mathf.Max(.0001f, Mathf.Abs(s.z)));
        }

        var sw = host.AddComponent<Level2LampSwitch>();
        sw.lamp = roomLamp;
        return sw;
    }

    /// <summary>同一盏灯可能被“灯本身”和“墙上面板”同时挂开关，RoomLamp 必须只有一份，否则两边各写一次状态会打架。</summary>
    static RoomLamp EnsureRoomLamp(Light source)
    {
        var roomLamp = source.GetComponent<RoomLamp>();
        if (!roomLamp) roomLamp = source.gameObject.AddComponent<RoomLamp>();
        roomLamp.lightSources = new[] { source };
        roomLamp.isOn = source.enabled;
        roomLamp.SetOn(source.enabled);      // AddComponent 时 OnEnable 已经跑过，这里按真实状态落一次
        return roomLamp;
    }

    /// <summary>
    /// 取灯“这一件东西”的世界包围盒：Light 自己、它下面、或者最近一两层父级里的网格。
    /// Unreal 导入的层级一般是 BP_Lamp → SM_Lamp(网格) → LightComponent0(Light)，
    /// 所以往上找一层就够。再往上就是整间屋子了，用尺寸上限挡掉。
    /// </summary>
    static bool TryModelBounds(Transform t, out Bounds bounds)
    {
        bounds = default;
        bool has = false;
        for (int depth = 0; depth < 3 && t; depth++, t = t.parent)
        {
            var renderers = t.GetComponentsInChildren<Renderer>(false);
            if (renderers.Length == 0) continue;
            if (renderers.Length > 8) break;      // 这个层级已经是整组家具/房间了，别用

            foreach (var r in renderers)
            {
                if (!r) continue;
                if (!has) { bounds = r.bounds; has = true; }
                else bounds.Encapsulate(r.bounds);
            }
            if (has) break;
        }

        if (!has) return false;
        // 灯不会比 3 米还大；超出说明抓到了错误的层级（房间、楼层），宁可不加。
        if (bounds.size.x > 3f || bounds.size.y > 3f || bounds.size.z > 3f) return false;
        return true;
    }
}
