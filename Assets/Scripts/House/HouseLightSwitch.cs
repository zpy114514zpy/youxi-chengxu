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
    }
}
