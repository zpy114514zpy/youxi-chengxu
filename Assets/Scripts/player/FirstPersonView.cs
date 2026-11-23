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
    readonly Transform[] upper = new Transform[2], lower = new Transform[2], hands = new Transform[2], fingers = new Transform[2];
    readonly Quaternion[] restingUpper = new Quaternion[2], restingLower = new Quaternion[2], restingHands = new Quaternion[2];
    Rigidbody body;
    readonly Quaternion[] appliedUpper = new Quaternion[2], appliedLower = new Quaternion[2], appliedHands = new Quaternion[2];
    float yaw, pitch, eyeHeight, reminderUntil;
    bool runAttempt, interacting;
    // 推东西时的双手：把两只手真正按到被推物体的表面上，物体往前走、手跟着走，
    // 不会再出现“箱子自己跑了、手却不见了”的画面。
    PushProp pushingProp;
    float pushBlend;
    Transform spine;
    float pushLean;
    public void SetPush(PushProp prop) { pushingProp = prop; }
    float crouchOffset, targetCrouchOffset;
    public void SetCrouchOffset(float offset) { targetCrouchOffset = offset; }

    /// <summary>当前的水平视角 / 俯仰（存档、读档用）。</summary>
    public float Yaw => yaw;
    public float Pitch => pitch;

    /// <summary>读档时直接把视角摆到存下来的方向，不再从角色根节点的旋转推。</summary>
    public void SetLook(float yawDegrees, float pitchDegrees = 0f)
    {
        yaw = yawDegrees;
        pitch = Mathf.Clamp(pitchDegrees, -75f, maximumLookDown);
    }
    Font font;
    Mesh visibleMesh, originalMesh;
    SkinnedMeshRenderer skin;
    FullBodyShadow fullBodyShadow;

    public void Initialize(Animator character, CapsuleCollider collider, float standingEyeHeight = 0.9f)
    {
        animator = character;
        capsule = collider;
        body = collider.attachedRigidbody;
        view = Camera.main;
        var bones = animator.GetComponentsInChildren<Transform>();
        head = System.Array.Find(bones, b => b.name == "mixamorig:Head");
        spine = System.Array.Find(bones, b => b.name == "mixamorig:Spine1");
        if (!spine) spine = System.Array.Find(bones, b => b.name == "mixamorig:Spine");
        for (int i = 0; i < 2; i++)
        {
            string side = i == 0 ? "mixamorig:Left" : "mixamorig:Right";
            upper[i] = System.Array.Find(bones, b => b.name == side + "Arm");
            lower[i] = System.Array.Find(bones, b => b.name == side + "ForeArm");
            hands[i] = System.Array.Find(bones, b => b.name == side + "Hand");
            fingers[i] = System.Array.Find(bones, b => b.name == side + "HandMiddle1");
            if (upper[i]) restingUpper[i] = upper[i].localRotation;
            if (lower[i]) restingLower[i] = lower[i].localRotation;
            if (hands[i]) restingHands[i] = hands[i].localRotation;
            appliedUpper[i] = restingUpper[i];
            appliedLower[i] = restingLower[i];
            appliedHands[i] = restingHands[i];
        }
        yaw = animator.transform.eulerAngles.y;
        eyeHeight = standingEyeHeight;
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
        if(StoryProgress.Instance && StoryProgress.Instance.IsReading)return;
        if(UiInputBlocker.Active)return;      // 密码键盘等独占输入的界面打开时，视角不要动
        var inventory=GetComponent<PlayerInventory>();
        if(inventory && inventory.IsOpen)return;
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        if (Input.GetMouseButtonDown(0) && !(StoryProgress.Instance && StoryProgress.Instance.IsShowingPage))
        {
            // 剧情/提示面板在屏幕上时鼠标要留给面板上的“上一步”按钮，单击不要抢回来。
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
        crouchOffset = Mathf.MoveTowards(crouchOffset, targetCrouchOffset, Time.deltaTime * 2f);
        eye.y = capsule.bounds.min.y + eyeHeight - crouchOffset;
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

        // 推东西：不管鼠标怎么抬，双手都按在物体表面上，跟着物体一起移动。
        bool pushing = pushingProp != null;
        pushBlend = Mathf.MoveTowards(pushBlend, pushing ? 1f : 0f, Time.deltaTime * 7f);
        if (pushing && pushBlend > .01f)
        {
            PosePushArms(pushBlend);
            return;
        }

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
    // ---------- 推东西时的双手 IK ----------

    /// <summary>
    /// 把两只手按在被推物体的顶面近边上：目标点每帧从物体包围盒现算，
    /// 所以物体往前走，手也跟着往前走，视觉上始终是“手在推”。
    /// </summary>
    void PosePushArms(float weight)
    {
        if (!pushingProp) return;
        Collider col = pushingProp.GetComponentInChildren<Collider>();
        if (!col) return;

        Bounds b = col.bounds;
        Vector3 origin = capsule.bounds.center;
        Vector3 toward = Vector3.ProjectOnPlane(b.center - origin, Vector3.up);
        if (toward.sqrMagnitude < .0004f) toward = animator.transform.forward;
        toward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, toward).normalized;
        float halfDepth = Mathf.Abs(toward.x) * b.extents.x + Mathf.Abs(toward.z) * b.extents.z;
        Vector3 nearEdge = b.center - toward * halfDepth;
        float top = b.max.y;

        // 先把两只手的目标点算出来（物体顶面、靠玩家一侧的边缘）。
        var target = new Vector3[2];
        for (int i = 0; i < 2; i++)
        {
            float side = i == 0 ? -1f : 1f;
            Vector3 p = nearEdge + right * (side * .07f) + toward * .045f;
            p.y = top + .02f;
            target[i] = col.ClosestPoint(p);
            target[i].y = Mathf.Max(target[i].y, top) + .055f;
        }

        // 凳子矮、胳膊短：手够不到时把上半身往前探一点，看起来就是“弯着腰在推”。
        float reach = 0f, need = 0f;
        for (int i = 0; i < 2; i++)
        {
            if (!upper[i] || !lower[i] || !hands[i]) continue;
            reach = Mathf.Max(reach, (lower[i].position - upper[i].position).magnitude
                + (hands[i].position - lower[i].position).magnitude);
            need = Mathf.Max(need, Vector3.Distance(upper[i].position, target[i]));
        }
        float deficit = Mathf.Max(0f, need - reach + .015f);
        float leanTarget = Mathf.Clamp(deficit * 130f, 0f, 30f);
        pushLean = Mathf.MoveTowards(pushLean, leanTarget, Time.deltaTime * 90f);
        if (spine && pushLean > .15f)
            spine.rotation = Quaternion.AngleAxis(pushLean, right) * spine.rotation;

        for (int i = 0; i < 2; i++)
        {
            if (!upper[i] || !lower[i] || !hands[i]) continue;
            float side = i == 0 ? -1f : 1f;
            Vector3 handTarget = Vector3.Lerp(hands[i].position, target[i], weight);
            SolveArm(i, handTarget, right * side + Vector3.down * .9f);

            // 手腕再压平一点，让手指贴着物体表面，而不是戳进去。
            if (fingers[i])
            {
                Vector3 want = handTarget + toward * .055f - Vector3.up * .015f;
                RotateBoneTo(hands[i], fingers[i].position, want);
            }
        }
    }

    /// <summary>两段式手臂 IK：先算肘部，再把上臂/前臂分别转过去。</summary>
    void SolveArm(int i, Vector3 target, Vector3 pole)
    {
        Transform u = upper[i], l = lower[i], h = hands[i];
        Vector3 shoulder = u.position;
        Vector3 elbowNow = l.position;
        Vector3 handNow = h.position;
        float upperLength = (elbowNow - shoulder).magnitude;
        float lowerLength = (handNow - elbowNow).magnitude;
        if (upperLength < .001f || lowerLength < .001f) return;

        Vector3 toTarget = target - shoulder;
        float reach = upperLength + lowerLength;
        if (toTarget.magnitude > reach + .02f && !warnedReach)
        {
            warnedReach = true;
            Debug.Log("[推手] 物体表面超出臂展：目标 " + toTarget.magnitude.ToString("F2")
                + " m / 臂展 " + reach.ToString("F2") + " m", this);
        }
        float distance = Mathf.Clamp(toTarget.magnitude, .02f, reach - .004f);
        Vector3 dir = toTarget.sqrMagnitude > .000001f ? toTarget.normalized : animator.transform.forward;

        Vector3 poleDir = Vector3.ProjectOnPlane(pole, dir);
        if (poleDir.sqrMagnitude < .0001f) poleDir = Vector3.ProjectOnPlane(Vector3.down, dir);
        if (poleDir.sqrMagnitude < .0001f) poleDir = Vector3.forward;
        poleDir.Normalize();

        float cos = Mathf.Clamp((upperLength * upperLength + distance * distance - lowerLength * lowerLength)
            / (2f * upperLength * distance), -1f, 1f);
        float bend = Mathf.Acos(cos) * Mathf.Rad2Deg;
        Vector3 axis = Vector3.Cross(dir, poleDir);
        if (axis.sqrMagnitude < .000001f) axis = Vector3.right;
        Vector3 elbowDir = Quaternion.AngleAxis(bend, axis.normalized) * dir;
        Vector3 elbow = shoulder + elbowDir * upperLength;

        RotateBoneTo(u, elbowNow, elbow);
        RotateBoneTo(l, h.position, target);
    }

    bool warnedReach;

    static void RotateBoneTo(Transform bone, Vector3 from, Vector3 to)
    {
        Vector3 a = from - bone.position;
        Vector3 b = to - bone.position;
        if (a.sqrMagnitude < .0000001f || b.sqrMagnitude < .0000001f) return;
        bone.rotation = Quaternion.FromToRotation(a, b) * bone.rotation;
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







