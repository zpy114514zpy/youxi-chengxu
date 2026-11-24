using UnityEngine;

public sealed class HouseLightSwitch : MonoBehaviour
{
    public RoomLamp[] lamps;
    public bool IsOn
    {
        get { if (lamps != null) foreach (var lamp in lamps) if (lamp && lamp.isOn) return true; return false; }
    }
    public void Toggle()
    {
        bool next = !IsOn;
        if (lamps != null) foreach (var lamp in lamps) if (lamp) lamp.SetOn(next);
        // 开灯 / 关灯：同一段“咔哒”，开灯音高略高一点，听感上更“亮”。
        GameAudio.PlayLamp(next ? 1.04f : .96f);
        if (next && StoryProgress.Instance) StoryProgress.Instance.OnLampTurnedOn();
    }
}
