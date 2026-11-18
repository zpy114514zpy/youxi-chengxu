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
        inventory=GetComponent<PlayerInventory>();
        if (!inventory) inventory=gameObject.AddComponent<PlayerInventory>();
        if (!GetComponent<CrouchPose>()) gameObject.AddComponent<CrouchPose>();
        shoes = new ShoeGrounding(animator, capsule, FloorMask);
        standingHeight = capsule.height;
        standingCenter = capsule.center;
        // Earlier scenes serialized E for pushing; Q is now reserved for that action.
        if (pushKey == KeyCode.E) pushKey = KeyCode.Q;
        if (pickupKey == KeyCode.F) pickupKey = KeyCode.None;
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
        if(inventory && inventory.IsOpen){direction=Vector3.zero;pushHeld=false;return;}
        Transform basis = orientation;
        if (!basis && Camera.main) basis = Camera.main.transform;
        Vector3 forward = basis ? Vector3.ProjectOnPlane(basis.forward, Vector3.up).normalized : Vector3.forward;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        direction = Vector3.ClampMagnitude(forward * Input.GetAxisRaw("Vertical")
            + Vector3.Cross(Vector3.up, forward) * Input.GetAxisRaw("Horizontal"), 1f);
        firstPerson?.CheckRunAttempt(Input.GetKey(runKey), direction.sqrMagnitude > 0.001f);
        pushHeld = Input.GetKey(pushKey);
        UpdateCrouch();
        if (pickupKey != KeyCode.None && onFloor && !pushHeld && Time.time >= pickupUntil && Input.GetKeyDown(pickupKey))
        {
            pickupUntil = Time.time + pickupDuration;
            Play(Pickup);
        }

    }

    int FloorMask => Ground.value == 0 ? Physics.DefaultRaycastLayers : Ground.value;

    void UpdateCrouch()
    {
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
        Bounds bounds = capsule.bounds;
        // Probe a footprint so a seam between two floor tiles does not interrupt walking.
        float radius = Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.8f;
        bool grounded = Physics.SphereCast(bounds.center, radius, Vector3.down, out var support,
            bounds.extents.y - radius + 0.08f, FloorMask, QueryTriggerInteraction.Ignore)
            && support.rigidbody != body && support.normal.y > 0.55f;
        onFloor = grounded;
        bool taking = Time.time < pickupUntil;
        bool moving = !taking && direction.sqrMagnitude > 0.001f;
        RaycastHit obstacle = default;
        bool pushing = moving && pushHeld && grounded &&
            body.SweepTest(direction.normalized, out obstacle, pushReach, QueryTriggerInteraction.Ignore)
            && Mathf.Abs(obstacle.normal.y) < 0.65f && obstacle.collider.GetComponentInParent<PushProp>();
        float speed = pushing ? Mathf.Min(pushSpeed, walkSpeed) : walkSpeed;
        if(crouched)speed*=.5f;
        Vector3 desired = moving ? direction * speed : Vector3.zero;
        Vector3 horizontal = Vector3.ProjectOnPlane(body.velocity, Vector3.up);
        float acceleration = Mathf.Max(1f, moveSpeed * groundDrag) * (grounded ? 1f : airMultiplier);
        horizontal = Vector3.MoveTowards(horizontal, desired, acceleration * dt);
        body.velocity = horizontal + Vector3.up * body.velocity.y;
        body.AddForce(Physics.gravity * (gravityMultiplier - 1f), ForceMode.Acceleration);
        if(pushing) obstacle.collider.GetComponentInParent<PushProp>().TryPush(direction,dt);
        Play(taking ? Pickup : pushing ? Push : moving ? Walk : Idle);
        animator.speed = playing == Idle ? 0f : 1f;
        firstPerson?.SetInteraction(taking || pushing);
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


