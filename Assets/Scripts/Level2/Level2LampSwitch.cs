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
    /// 灯的物体上没有碰撞体，而 PlayerInventory 的交互球扫会跳过 trigger，
    /// 所以这里也要补一个非 trigger 的小盒子，让准星能“打中”灯。
    /// </summary>
    public static Level2LampSwitch AttachTo(Light source)
    {
        if (!source) return null;
        var existing = source.GetComponent<Level2LampSwitch>();
        if (existing) return existing;

        var roomLamp = source.GetComponent<RoomLamp>();
        if (!roomLamp) roomLamp = source.gameObject.AddComponent<RoomLamp>();
        roomLamp.lightSources = new[] { source };
        roomLamp.isOn = source.enabled;
        roomLamp.SetOn(source.enabled);      // AddComponent 时 OnEnable 已经跑过，这里按真实状态落一次

        var col = source.GetComponent<Collider>();
        if (!col || col.isTrigger)     // 灯上只有 trigger（Unreal 的灯常常带一个触发球）时，球扫也会跳过它
        {
            var b = source.gameObject.AddComponent<BoxCollider>();
            // 灯模型常常带 100 倍的导入缩放，尺寸要按世界尺寸换算，否则一个小盒子会变成 30 米。
            var s = source.transform.lossyScale;
            b.size = new Vector3(
                .30f / Mathf.Max(.0001f, Mathf.Abs(s.x)),
                .30f / Mathf.Max(.0001f, Mathf.Abs(s.y)),
                .30f / Mathf.Max(.0001f, Mathf.Abs(s.z)));
        }

        var sw = source.gameObject.AddComponent<Level2LampSwitch>();
        sw.lamp = roomLamp;
        return sw;
    }
}
