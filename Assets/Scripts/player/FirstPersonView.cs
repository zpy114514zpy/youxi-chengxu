using UnityEngine;
using System.Collections.Generic;

[DefaultExecutionOrder(100)]
public class FirstPersonView : MonoBehaviour
{
    [Range(0.1f, 5f)] public float mouseSensitivity = 1.6f;
    [Range(50f, 90f)] public float fieldOfView = 68f;
    [Range(0f, 85f)] public float maximumLookDown = 78f;
    [Range(0.05f, 0.3f)] public float eyeForwardOffset = 0.17f;
    [Range(0f, 1f)] public float breathingAmount = 0.15f;
    public string runReminder = "爸爸妈妈说在家里不能跑步";
    public float reminderSeconds = 2.5f;

    Animator animator;
    Camera view;
    CapsuleCollider capsule;
    Transform head;
    readonly Transform[] upper = new Transform[2], lower = new Transform[2], hands = new Transform[2];
    readonly Quaternion[] restingUpper = new Quaternion[2], restingLower = new Quaternion[2], restingHands = new Quaternion[2];
    Rigidbody body;
    readonly Quaternion[] appliedUpper = new Quaternion[2], appliedLower = new Quaternion[2], appliedHands = new Quaternion[2];
    float yaw, pitch, eyeHeight, reminderUntil;
    bool runAttempt, interacting;
    Font font;
    Mesh visibleMesh, originalMesh;
    SkinnedMeshRenderer skin;
    FullBodyShadow fullBodyShadow;

    public void Initialize(Animator character, CapsuleCollider collider)
    {
        animator = character;
        capsule = collider;
        body = collider.attachedRigidbody;
        view = Camera.main;
        var bones = animator.GetComponentsInChildren<Transform>();
        head = System.Array.Find(bones, b => b.name == "mixamorig:Head");
        for (int i = 0; i < 2; i++)
        {
            string side = i == 0 ? "mixamorig:Left" : "mixamorig:Right";
            upper[i] = System.Array.Find(bones, b => b.name == side + "Arm");
            lower[i] = System.Array.Find(bones, b => b.name == side + "ForeArm");
            hands[i] = System.Array.Find(bones, b => b.name == side + "Hand");
            if (upper[i]) restingUpper[i] = upper[i].localRotation;
            if (lower[i]) restingLower[i] = lower[i].localRotation;
            if (hands[i]) restingHands[i] = hands[i].localRotation;
            appliedUpper[i] = restingUpper[i];
            appliedLower[i] = restingLower[i];
            appliedHands[i] = restingHands[i];
        }
        yaw = animator.transform.eulerAngles.y;
        eyeHeight = head ? head.position.y - capsule.bounds.min.y + 0.045f : capsule.bounds.size.y * 0.9f;
        font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 28);
        HideHead();
        if (view)
        {
            view.nearClipPlane = 0.02f;
            view.fieldOfView = fieldOfView;
        }
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        LateUpdate();
    }

    void HideHead()
    {
        skin = animator.GetComponentInChildren<SkinnedMeshRenderer>();
        if (!skin || !head) return;
        originalMesh = skin.sharedMesh;
        fullBodyShadow = gameObject.AddComponent<FullBodyShadow>();
        fullBodyShadow.Initialize(skin, originalMesh, head.parent);
        visibleMesh = Instantiate(originalMesh);
        var weights = visibleMesh.boneWeights;
        bool[] hidden = new bool[skin.bones.Length];
        for (int i = 0; i < hidden.Length; i++)
            hidden[i] = skin.bones[i] && (skin.bones[i] == head || skin.bones[i].IsChildOf(head));
        var baked = new Mesh();
        skin.BakeMesh(baked);
        var vertices = baked.vertices;
        float neckHeight = head.parent.position.y - 0.025f;
        bool[] omit = new bool[weights.Length];
        for (int i = 0; i < weights.Length; i++)
        {
            var w = weights[i];
            float sum = (hidden[w.boneIndex0] ? w.weight0 : 0) + (hidden[w.boneIndex1] ? w.weight1 : 0)
                + (hidden[w.boneIndex2] ? w.weight2 : 0) + (hidden[w.boneIndex3] ? w.weight3 : 0);
            omit[i] = sum > 0.3f || skin.transform.TransformPoint(vertices[i]).y > neckHeight;
        }
        for (int sub = 0; sub < visibleMesh.subMeshCount; sub++)
        {
            var triangles = visibleMesh.GetTriangles(sub);
            var kept = new List<int>();
            for (int i = 0; i < triangles.Length; i += 3)
            {
                if (omit[triangles[i]] || omit[triangles[i + 1]] || omit[triangles[i + 2]]) continue;
                kept.Add(triangles[i]); kept.Add(triangles[i + 1]); kept.Add(triangles[i + 2]);
            }
            visibleMesh.SetTriangles(kept, sub);
        }
        Destroy(baked);
        skin.sharedMesh = visibleMesh;
    }

    public void CheckRunAttempt(bool held, bool moving)
    {
        bool attempting = held && moving;
        if (attempting && !runAttempt) reminderUntil = Time.unscaledTime + reminderSeconds;
        runAttempt = attempting;
    }

    public void SetInteraction(bool active) { interacting = active; }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        if (Input.GetMouseButtonDown(0))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        if (Cursor.lockState != CursorLockMode.Locked) return;
        yaw += Input.GetAxisRaw("Mouse X") * mouseSensitivity;
        pitch = Mathf.Clamp(pitch - Input.GetAxisRaw("Mouse Y") * mouseSensitivity, -75f, maximumLookDown);
    }

    void LateUpdate()
    {
        if (!view || !animator) return;
        if (skin && visibleMesh) skin.sharedMesh = visibleMesh;
        if (head) head.parent.localScale = Vector3.one * 0.001f;
        animator.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        Vector3 eye = capsule.bounds.center;
        eye.y = capsule.bounds.min.y + eyeHeight;
        // Keep the eye outside the shirt, including when looking down into the collar.
        // The eye must stay outside nearby doors/furniture even though its normal
        // offset is slightly in front of the character's collision capsule.
        float safeOffset = eyeForwardOffset;
        foreach (var hit in Physics.SphereCastAll(eye, 0.05f, animator.transform.forward,
            eyeForwardOffset, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.rigidbody == body || hit.transform.IsChildOf(transform)) continue;
            safeOffset = Mathf.Min(safeOffset, Mathf.Max(0f, hit.distance - 0.01f));
        }
        eye += animator.transform.forward * safeOffset;
        pitch = Mathf.Clamp(pitch, -75f, maximumLookDown);
        view.transform.SetPositionAndRotation(eye, Quaternion.Euler(pitch, yaw, 0f));
        if (interacting)
        {
            for (int i = 0; i < 2; i++)
            {
                if (!upper[i] || !lower[i] || !hands[i]) continue;
                appliedUpper[i] = upper[i].localRotation;
                appliedLower[i] = lower[i].localRotation;
                appliedHands[i] = hands[i].localRotation;
            }
            return;
        }
        bool walkingForward = Input.GetKey(KeyCode.W) && body
            && Vector3.ProjectOnPlane(body.velocity, Vector3.up).sqrMagnitude > 0.01f;
        float blend = 1f - Mathf.Exp(-12f * Time.deltaTime);
        float breath = Mathf.Sin(Time.time * 1.6f) * breathingAmount;
        for (int i = 0; i < 2; i++)
        {
            if (!upper[i] || !lower[i] || !hands[i]) continue;
            Quaternion relaxed = restingUpper[i] * Quaternion.Euler(breath, 0f, 0f);
            appliedUpper[i] = Quaternion.Slerp(appliedUpper[i], walkingForward ? upper[i].localRotation : relaxed, blend);
            appliedLower[i] = Quaternion.Slerp(appliedLower[i], walkingForward ? lower[i].localRotation : restingLower[i], blend);
            appliedHands[i] = Quaternion.Slerp(appliedHands[i], walkingForward ? hands[i].localRotation : restingHands[i], blend);
            upper[i].localRotation = appliedUpper[i];
            lower[i].localRotation = appliedLower[i];
            hands[i].localRotation = appliedHands[i];
        }
    }
    void OnGUI()
    {
        if (Time.unscaledTime >= reminderUntil) return;
        float width = Mathf.Min(Screen.width - 32f, 640f);
        var rect = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.5f - 45f, width, 90f);
        var style = new GUIStyle(GUI.skin.box) {
            alignment = TextAnchor.MiddleCenter, font = font,
            fontSize = Mathf.Clamp(Mathf.RoundToInt(Screen.width / 45f), 18, 30),
            wordWrap = true
        };
        GUI.Box(rect, runReminder, style);
    }

    void OnDestroy()
    {
        if (skin && originalMesh) skin.sharedMesh = originalMesh;
        if (fullBodyShadow) Destroy(fullBodyShadow);
        if (visibleMesh) Destroy(visibleMesh);
        if (font) Destroy(font);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }
}







