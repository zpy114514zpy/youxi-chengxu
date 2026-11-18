using UnityEngine;

[ExecuteAlways, DisallowMultipleComponent]
public sealed class RoomLamp : MonoBehaviour
{
    [Header("灯具开关（默认关闭）")]
    public bool isOn;
    [Tooltip("只有这个灯具发出的光；关灯时全部关闭。")]
    public Light[] lightSources;
    public GameObject glowingBulb;
    bool applied;
    void OnEnable() { Apply(); }
    void Update() { if (applied != isOn) Apply(); }
    public void SetOn(bool value) { isOn = value; Apply(); }
    void Apply()
    {
        applied = isOn;
        if (lightSources != null)
            foreach (var lightSource in lightSources)
                if (lightSource) lightSource.enabled = isOn && isActiveAndEnabled;
        if (glowingBulb) glowingBulb.SetActive(isOn && isActiveAndEnabled);
    }
    void OnDisable()
    {
        if (lightSources != null)
            foreach (var lightSource in lightSources) if (lightSource) lightSource.enabled = false;
        if (glowingBulb) glowingBulb.SetActive(false);
    }
}
