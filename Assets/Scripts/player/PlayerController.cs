using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [Header("移动")]
    [Min(0f)] public float walkSpeed = 2f;
    [Min(0f)] public float moveSpeed = 7f;
    [Min(0f)] public float groundDrag = 7f;
    [HideInInspector] public float jumpForce = 12f;
    [HideInInspector] public float jumpCooldown = 0.25f;
    public float airMultiplier = 0.4f;
    [Min(0f)] public float gravityMultiplier = 2f;
    [HideInInspector] public KeyCode jumpKey = KeyCode.Space;
    public float playerHeight = 2f;
    public LayerMask Ground;
    public Transform orientation;

    [Header("角色动作")]
    public KeyCode runKey = KeyCode.LeftShift;
    [HideInInspector] public KeyCode pickupKey = KeyCode.None;
    public KeyCode pushKey = KeyCode.Q;
    public KeyCode crouchKey = KeyCode.LeftControl;
    [Range(0.5f, 0.9f)] public float crouchHeightRatio = 0.65f;
    [Min(0f)] public float pushSpeed = 1f;
    [Min(0f)] public float pushReach = 0.25f;

    static readonly int Idle = State("Idle"), Walk = State("Walk"),
        Pickup = State("Pickup"), Push = State("Push");
    Rigidbody body;
    Animator animator;
    CapsuleCollider capsule;
    ShoeGrounding shoes;
    bool onFloor;
    Vector3 direction;
    bool pushHeld;
    float pickupUntil, pickupDuration;
    int playing;

    // ---- 脚步声 ----
    // 脚步声不能按“在走/不在走”开关循环播放：那段音频里一秒才一步，和 2 m/s 的走速完全脱节。
    // 改成累计实际走过的位移，每满一步响一次 —— 走多快就响多快，停下就没声。
    Vector3 lastWalkPos;
    bool walkTracked;

    // ---- 站在可踩物（凳子）上的状态 ----
    // 角色是带重力的动力学刚体，凳子又不在 Ground 层，光瞬移上去会被重力立刻拽回地面
    // （表现为“按 E 后往下掉，站在凳子边而不是中心”）。所以站上去时由这里接管：
    // 关重力 + 每帧钉在凳面中心，直到玩家主动迈下来。
    bool platformLock;
    Vector3 platformTarget;
    float platformFeetOffset;
    Vector3 platformCapOffset;   // 胶囊轴心相对角色根节点的水平偏移（胶囊常常不在根节点正下方）
    float stepOffTimer;
    public bool OnPlatform => platformLock;

    // ---- 坐在椅子上 ----
    // 和“站到凳子上”共用平台锁定（关重力 + 每帧钉住），区别只是额外压低视角，
    // 让镜头停在坐姿高度；起身或者按方向键就解掉。
    bool sitting;
    public bool Sitting => sitting;

    [Header("游戏视角")]
    [Tooltip("站立时镜头距脚底的高度（米），不随待机动画上下晃动。")]
    [Range(0.6f, 1.5f)] public float standingEyeHeight = 0.9f;
    [HideInInspector] public bool followPlayer = true;
    FirstPersonView firstPerson;
    PlayerLampInteraction lampInteraction;
    float standingHeight;
    Vector3 standingCenter;
    bool crouched;
    public bool IsCrouched => crouched;
    public float CrouchDrop => capsule ? (standingHeight-capsule.height)*Mathf.Abs(capsule.transform.lossyScale.y) : 0;
    PlayerInventory inventory;
    public void PlayPickup(){pickupUntil=Time.time+Mathf.Max(.5f,pickupDuration);Play(Pickup);}


    void Start()
    {
        if (!GetComponent<StoryProgress>()) gameObject.AddComponent<StoryProgress>();
        if (!GetComponent<FutureFog>()) gameObject.AddComponent<FutureFog>();
        inventory=GetComponent<PlayerInventory>();
        if (!inventory) inventory=gameObject.AddComponent<PlayerInventory>();
        if (!GetComponent<CrouchPose>()) gameObject.AddComponent<CrouchPose>();
        shoes = new ShoeGrounding(animator, capsule, FloorMask);
        standingHeight = capsule.height;
        standingCenter = capsule.center;
        // Earlier scenes serialized E for pushing; Q is now reserved for that action.
        if (pushKey == KeyCode.E) pushKey = KeyCode.Q;
        // 场景里序列化的 0.25 太贴脸，箱子稍微挪一点就够不到，给一个下限。
        pushReach = Mathf.Max(pushReach, .45f);
        if (pickupKey == KeyCode.F || pickupKey == KeyCode.B) pickupKey = KeyCode.None;
        firstPerson = gameObject.AddComponent<FirstPersonView>();
        firstPerson.Initialize(animator, capsule, standingEyeHeight);
        lampInteraction = GetComponent<PlayerLampInteraction>();
        if (!lampInteraction) lampInteraction = gameObject.AddComponent<PlayerLampInteraction>();
    }

    public void PlantFeet()
    {
        if (onFloor) shoes?.Plant();
    }
    static int State(string name) => Animator.StringToHash("Base Layer." + name);

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        animator = GetComponentInChildren<Animator>();
        capsule = GetComponentInChildren<CapsuleCollider>();
        body.freezeRotation = true;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        foreach (var clip in animator.runtimeAnimatorController.animationClips)
            if (clip.name == "Pickup") pickupDuration = clip.length;
    }

    void Update()
    {
        if(UiInputBlocker.Active || StoryProgress.Instance && StoryProgress.Instance.IsReading)
        {
            direction=Vector3.zero;pushHeld=false;
            GameAudio.SetWalking(false);GameAudio.SetPushing(false);
            return;
        }
        UpdateCrouch();
        if(inventory && inventory.IsOpen)
        {
            direction=Vector3.zero;pushHeld=false;
            GameAudio.SetWalking(false);GameAudio.SetPushing(false);
            return;
        }
        Transform basis = orientation;
        if (!basis && Camera.main) basis = Camera.main.transform;
        Vector3 forward = basis ? Vector3.ProjectOnPlane(basis.forward, Vector3.up).normalized : Vector3.forward;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        direction = Vector3.ClampMagnitude(forward * Input.GetAxisRaw("Vertical")
            + Vector3.Cross(Vector3.up, forward) * Input.GetAxisRaw("Horizontal"), 1f);
        firstPerson?.CheckRunAttempt(Input.GetKey(runKey), direction.sqrMagnitude > 0.001f);
        pushHeld = Input.GetKey(pushKey);
        if (pickupKey != KeyCode.None && onFloor && !pushHeld && Time.time >= pickupUntil && Input.GetKeyDown(pickupKey))
        {
            pickupUntil = Time.time + pickupDuration;
            Play(Pickup);
        }

    }

    int FloorMask => Ground.value == 0 ? Physics.DefaultRaycastLayers : Ground.value;

    void UpdateCrouch()
    {
        // 坐着的时候视角由 SitOn 接管，别让“没按蹲键”把压低的那截视角又顶回去。
        if (sitting) return;
        SetCrouching(Input.GetKey(crouchKey));
    }

    public void SetCrouching(bool requested)
    {
        if (!requested && crouched)
        {
            float worldRadius = capsule.radius * Mathf.Max(Mathf.Abs(capsule.transform.lossyScale.x), Mathf.Abs(capsule.transform.lossyScale.z));
            Vector3 top = capsule.transform.TransformPoint(standingCenter + Vector3.up * (standingHeight * .5f - capsule.radius));
            Vector3 bottom = capsule.transform.TransformPoint(standingCenter - Vector3.up * (standingHeight * .5f - capsule.radius));
            foreach (var hit in Physics.OverlapCapsule(bottom, top, worldRadius * .95f, FloorMask, QueryTriggerInteraction.Ignore))
                if (hit.attachedRigidbody != body && !hit.transform.IsChildOf(transform)) return;
        }
        crouched = requested;
        float height = requested ? Mathf.Max(capsule.radius * 2f, standingHeight * crouchHeightRatio) : standingHeight;
        capsule.height = height;
        capsule.center = standingCenter - Vector3.up * ((standingHeight - height) * .5f);
        if (firstPerson) firstPerson.SetCrouchOffset((standingHeight - height) * Mathf.Abs(capsule.transform.lossyScale.y));
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        if (platformLock)
        {
            GameAudio.SetWalking(false);
            GameAudio.SetPushing(false);
            UpdatePlatform(dt);
            return;
        }
        Bounds bounds = capsule.bounds;
        // Probe a footprint so a seam between two floor tiles does not interrupt walking.
        float radius = Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.8f;
        bool grounded = Physics.SphereCast(bounds.center, radius, Vector3.down, out var support,
            bounds.extents.y - radius + 0.08f, FloorMask, QueryTriggerInteraction.Ignore)
            && support.rigidbody != body && support.normal.y > 0.55f;
        onFloor = grounded;
        bool taking = Time.time < pickupUntil;
        bool moving = !taking && direction.sqrMagnitude > 0.001f;

        // 推动：先问箱子这一帧能移动多远，角色再按同样速度跟上。
        // 原来角色按 1.0 m/s 走、箱子只有 0.8 m/s，人会一直顶进箱子，画面一顿一顿地抖。
        PushProp pushProp = null;
        Vector3 pushNormal = Vector3.zero;
        float pushGap = 0f;
        float pushed = 0f;
        bool pushing = moving && pushHeld && grounded && FindPushTarget(out pushProp, out pushNormal, out pushGap);
        if (pushing)
        {
            // 正面顶着箱面推时，把方向锁到箱面法线：箱子只走直线，不会横着打滑。
            Vector3 face = Vector3.ProjectOnPlane(-pushNormal, Vector3.up);
            Vector3 pushDir = face.sqrMagnitude > .01f && Vector3.Dot(face.normalized, direction.normalized) > .5f
                ? face.normalized : direction.normalized;
            pushed = pushProp.TryPush(pushDir, dt);
        }
        // 和物体保持“贴着”的距离：中间还隔着一段就稍微快一点跟上去，贴住之后严格按物体的速度走。
        // 只按物体速度走的话，玩家在 0.4 m 外按 Q 会一直保持 0.4 m，手永远够不到凳子。
        float speed = pushing
            ? Mathf.Min(walkSpeed, pushed / Mathf.Max(dt, .0001f) + Mathf.Max(0f, pushGap - .02f) * 6f)
            : walkSpeed;
        if(crouched)speed*=.5f;
        Vector3 desired = moving ? direction * speed : Vector3.zero;
        // Follow the stair ramp instead of continually driving into its risers.
        if (grounded && moving)
            desired = Vector3.ProjectOnPlane(desired, support.normal).normalized * speed;
        Vector3 desiredHorizontal = Vector3.ProjectOnPlane(desired, Vector3.up);
        Vector3 horizontal = Vector3.ProjectOnPlane(body.velocity, Vector3.up);
        float acceleration = Mathf.Max(1f, moveSpeed * groundDrag) * (grounded ? 1f : airMultiplier);
        horizontal = Vector3.MoveTowards(horizontal, desiredHorizontal, acceleration * dt);
        float vertical = body.velocity.y;
        if (grounded && desired.y > 0f) vertical = Mathf.Max(vertical, desired.y);
        // 小台阶/压条辅助：门槛、地板压边这类 3~30cm 的凸起，胶囊只靠推力会被顶住走不过去
        // （玩家表现就是“踩上去以后动不了”）。这里检测到矮台阶就轻轻抬一下。
        if (grounded && moving && horizontal.sqrMagnitude > .01f)
            vertical = Mathf.Max(vertical, StepAssist(horizontal.normalized));
        body.velocity = horizontal + Vector3.up * vertical;
        body.AddForce(Physics.gravity * (gravityMultiplier - 1f), ForceMode.Acceleration);
        Play(taking ? Pickup : pushing ? Push : moving ? Walk : Idle);
        // 推东西 / 走路的声音：推的时候循环推声；走路按实际走过的距离，一步一响。
        GameAudio.SetPushing(pushing);
        bool walking = moving && grounded && !taking && !pushing;
        GameAudio.SetWalking(walking);
        if (walking)
        {
            if (!walkTracked) { lastWalkPos = body.position; walkTracked = true; }
            Vector3 stepped = Vector3.ProjectOnPlane(body.position - lastWalkPos, Vector3.up);
            lastWalkPos = body.position;
            GameAudio.AddWalkDistance(stepped.magnitude);
        }
        else walkTracked = false;
        animator.speed = playing == Idle ? 0f : 1f;
        firstPerson?.SetInteraction(taking || pushing);
        // 推的时候把目标交给视角层，由它把双手按在物体表面（物体移动、手跟着移动）。
        firstPerson?.SetPush(pushing ? pushProp : null);
    }

    /// <summary>前方 3~30cm 的矮台阶：返回一个向上的速度，让胶囊能“蹭”上去；没有就返回 0。</summary>
    float StepAssist(Vector3 direction)
    {
        var bounds = capsule.bounds;
        float radius = Mathf.Max(.15f, Mathf.Min(bounds.extents.x, bounds.extents.z) * .8f);
        Vector3 feet = new Vector3(bounds.center.x, bounds.min.y + .06f, bounds.center.z);
        foreach (var hit in Physics.SphereCastAll(feet, radius, direction, radius + .34f, FloorMask,
                     QueryTriggerInteraction.Ignore))
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;
            if (hit.normal.y > .85f) continue;                         // 平地，不用抬
            float step = hit.collider.bounds.max.y - bounds.min.y;     // 这个物件顶部比脚底高多少
            if (step < .015f || step > .30f) continue;
            Vector3 lift = Vector3.up * (step + .05f);
            // 注意：不能用 Physics.CheckCapsule —— 它会把玩家自己的胶囊也算成障碍，
            // 于是永远判定“抬上去会顶头”，矮台阶永远上不去。
            bool blocked = false;
            foreach (var overlap in Physics.OverlapCapsule(bounds.min + lift, bounds.max + lift, radius * .85f,
                         FloorMask, QueryTriggerInteraction.Ignore))
            {
                if (overlap.transform.IsChildOf(transform)) continue;
                if (overlap.attachedRigidbody == body) continue;
                blocked = true; break;
            }
            if (blocked) continue;                                     // 抬上去会顶头
            return 2.2f;
        }
        return 0f;
    }
    // ---------- 可踩物（凳子） ----------

    /// <summary>踩到可站立物体上：关掉重力，把角色钉在给定位置（世界坐标的“脚底点”）。</summary>
    public void StandOnPlatform(Vector3 feetWorld)
    {
        if (!capsule) capsule = GetComponentInChildren<CapsuleCollider>();
        if (!body) body = GetComponent<Rigidbody>();
        platformFeetOffset = body.position.y - capsule.bounds.min.y;   // 根节点比脚底高多少
        // 胶囊轴心往往不在根节点正下方，按包围盒中心把这点偏移扣掉，人才真站在凳子正中。
        platformCapOffset = new Vector3(capsule.bounds.center.x - body.position.x, 0f,
                                        capsule.bounds.center.z - body.position.z);
        platformTarget = TargetOn(feetWorld);
        platformLock = true;
        stepOffTimer = 0f;
        body.useGravity = false;
        body.velocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        body.position = platformTarget;
        Physics.SyncTransforms();
    }

    /// <summary>凳子被挪动时跟着更新落脚点。</summary>
    public void KeepOnPlatform(Vector3 feetWorld)
    {
        if (!platformLock) return;
        platformTarget = TargetOn(feetWorld);
    }

    /// <summary>把“脚底点”换算成角色根节点该在的位置（扣掉胶囊的水平偏移）。</summary>
    Vector3 TargetOn(Vector3 feetWorld)
    {
        return new Vector3(feetWorld.x - platformCapOffset.x,
                           feetWorld.y + platformFeetOffset,
                           feetWorld.z - platformCapOffset.z);
    }

    public void LeavePlatform()
    {
        if (!platformLock) return;
        platformLock = false;
        if (body) body.useGravity = true;
        if (sitting)
        {
            sitting = false;
            if (firstPerson) firstPerson.SetCrouchOffset(0f);
        }
    }

    // ---------- 坐椅子（书桌前那把） ----------

    /// <summary>坐上椅子：钉在椅面上、把视角压到坐姿高度，直到玩家按 E 起身或者按方向键站起来。</summary>
    public void SitOn(Vector3 feetWorld, float eyeDrop)
    {
        StandOnPlatform(feetWorld);
        sitting = true;
        if (firstPerson) firstPerson.SetCrouchOffset(Mathf.Max(0f, eyeDrop));
    }

    /// <summary>起身：先试着往身后迈一步，迈不动就交还重力（椅子矮，掉下去也不会摔坏）。</summary>
    public void StandUp()
    {
        if (!sitting) return;
        sitting = false;
        if (firstPerson) firstPerson.SetCrouchOffset(0f);
        if (!StepOff(-transform.forward)) LeavePlatform();
    }

    /// <summary>
    /// 站在凳面上时接管移动：每帧把位置钉死，重力不再作用，所以不可能往下掉。
    /// 只有玩家按住方向键超过 0.22 秒，才会朝那个方向迈下去。
    /// </summary>
    void UpdatePlatform(float dt)
    {
        // 坐着的时候按方向键 = 起身（不是从凳子上迈下去，先站起来再走）。
        if (sitting && direction.sqrMagnitude > .001f) { StandUp(); return; }
        if (direction.sqrMagnitude > .001f) stepOffTimer += dt; else stepOffTimer = 0f;
        if (stepOffTimer > .22f && StepOff(direction)) return;

        body.velocity = Vector3.zero;
        body.MovePosition(platformTarget);
        onFloor = true;
        Play(Idle);
        animator.speed = 0f;
        firstPerson?.SetInteraction(false);
    }

    /// <summary>从凳面上迈下来：试四个方向，挑一个不撞墙的位置把角色放下去。</summary>
    public bool StepOff(Vector3 preferred)
    {
        if (!platformLock) return false;
        Vector3 fwd = Vector3.ProjectOnPlane(preferred, Vector3.up);
        if (fwd.sqrMagnitude < .01f) fwd = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (fwd.sqrMagnitude < .01f) fwd = Vector3.forward;
        fwd.Normalize();

        float radius = Mathf.Max(.18f, capsule.bounds.extents.x);
        Vector3[] dirs = { fwd, Vector3.Cross(Vector3.up, fwd), -fwd, -Vector3.Cross(Vector3.up, fwd) };
        foreach (var d in dirs)
        {
            Vector3 p = platformTarget + d * (radius + .3f);
            if (BlockedAt(p)) continue;
            platformLock = false;
            body.useGravity = true;
            body.position = p;
            body.velocity = d * walkSpeed;
            return true;
        }
        return false;
    }

    bool BlockedAt(Vector3 rootPos)
    {
        float r = Mathf.Max(.12f, capsule.bounds.extents.x * .85f);
        float feetY = rootPos.y - platformFeetOffset;
        Vector3 bottom = new Vector3(rootPos.x, feetY + r + .03f, rootPos.z);
        Vector3 top = new Vector3(rootPos.x, rootPos.y, rootPos.z);
        foreach (var hit in Physics.OverlapCapsule(bottom, top, r, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            if (hit.isTrigger || hit.transform.IsChildOf(transform)) continue;
            if (hit.GetComponentInParent<StoolStep>()) continue;   // 凳子本身不算障碍
            return true;
        }
        return false;
    }

    // ---------- 推箱子 ----------

    /// <summary>找脚下最近的可推物体，并记下接触面的法线（用来把推动方向锁到箱面法线）。</summary>
    bool FindPushTarget(out PushProp prop, out Vector3 normal, out float gap)
    {
        prop = null; normal = Vector3.zero; gap = 0f;
        float best = float.MaxValue;
        foreach (var hit in body.SweepTestAll(direction.normalized, pushReach, QueryTriggerInteraction.Ignore))
        {
            if (Mathf.Abs(hit.normal.y) >= .65f) continue;
            if (hit.collider.GetComponentInParent<PlayerController>()) continue;
            var candidate = hit.collider.GetComponentInParent<PushProp>();
            if (!candidate || hit.distance >= best) continue;
            best = hit.distance; prop = candidate; normal = hit.normal;
        }
        if (prop && best < float.MaxValue) gap = Mathf.Max(0f, best);
        return prop != null;
    }

    void Play(int state)
    {
        if (playing == state) return;
        animator.speed = 1f;
        if (state == Idle) animator.Play(state, 0, 0f);
        else animator.CrossFadeInFixedTime(state, 0.1f, 0, 0f);
        playing = state;
    }
}


