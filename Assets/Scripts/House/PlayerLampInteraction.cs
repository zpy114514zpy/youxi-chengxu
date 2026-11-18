using UnityEngine;

[DefaultExecutionOrder(-10)]
public sealed class PlayerLampInteraction : MonoBehaviour
{
    [Min(0.2f)] public float reach = 2f;
    HouseLightSwitch target;
    Font font;
    public bool IsTargetingSwitch => target;
    void Awake() { font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 24); }
    void Update()
    {
        target = null;
        var camera = Camera.main;
        if (!camera || Cursor.lockState != CursorLockMode.Locked) return;
        float nearest = reach;
        foreach (var hit in Physics.RaycastAll(camera.transform.position, camera.transform.forward, reach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(transform) || hit.distance >= nearest) continue;
            nearest = hit.distance;
            target = hit.collider.GetComponentInParent<HouseLightSwitch>();
        }
        if (target && Input.GetKeyDown(KeyCode.E)) target.Toggle();
    }
    void OnGUI()
    {
        if (!target) return;
        var style = new GUIStyle(GUI.skin.box) { font = font, fontSize = 22, alignment = TextAnchor.MiddleCenter };
        GUI.Box(new Rect(Screen.width * .5f - 100, Screen.height * .65f, 200, 44), target.IsOn ? "E 关灯" : "E 开灯", style);
    }
    void OnDestroy() { if (font) Destroy(font); }
}
