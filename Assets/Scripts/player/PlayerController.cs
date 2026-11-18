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
    public KeyCode pickupKey = KeyCode.F;
    public KeyCode pushKey = KeyCode.E;
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
    [HideInInspector] public bool followPlayer = true;
    FirstPersonView firstPerson;
    PlayerLampInteraction lampInteraction;


    void Start()
    {
        shoes = new ShoeGrounding(animator, capsule, FloorMask);
        firstPerson = gameObject.AddComponent<FirstPersonView>();
        firstPerson.Initialize(animator, capsule);
        lampInteraction = GetComponent<PlayerLampInteraction>();
        if (!lampInteraction) lampInteraction = gameObject.AddComponent<PlayerLampInteraction>();
    }

    void LateUpdate()
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
        Transform basis = orientation;
        if (!basis && Camera.main) basis = Camera.main.transform;
        Vector3 forward = basis ? Vector3.ProjectOnPlane(basis.forward, Vector3.up).normalized : Vector3.forward;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;
        direction = Vector3.ClampMagnitude(forward * Input.GetAxisRaw("Vertical")
            + Vector3.Cross(Vector3.up, forward) * Input.GetAxisRaw("Horizontal"), 1f);
        firstPerson?.CheckRunAttempt(Input.GetKey(runKey), direction.sqrMagnitude > 0.001f);
        pushHeld = Input.GetKey(pushKey) && !(lampInteraction && lampInteraction.IsTargetingSwitch);
        if (onFloor && !pushHeld && Time.time >= pickupUntil && Input.GetKeyDown(pickupKey))
        {
            pickupUntil = Time.time + pickupDuration;
            Play(Pickup);
        }

    }

    int FloorMask => Ground.value == 0 ? Physics.DefaultRaycastLayers : Ground.value;

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
            && Mathf.Abs(obstacle.normal.y) < 0.65f;
        float speed = pushing ? Mathf.Min(pushSpeed, walkSpeed) : walkSpeed;
        Vector3 desired = moving ? direction * speed : Vector3.zero;
        Vector3 horizontal = Vector3.ProjectOnPlane(body.velocity, Vector3.up);
        float acceleration = Mathf.Max(1f, moveSpeed * groundDrag) * (grounded ? 1f : airMultiplier);
        horizontal = Vector3.MoveTowards(horizontal, desired, acceleration * dt);
        body.velocity = horizontal + Vector3.up * body.velocity.y;
        body.AddForce(Physics.gravity * (gravityMultiplier - 1f), ForceMode.Acceleration);
        if (pushing && obstacle.rigidbody && !obstacle.rigidbody.isKinematic)
        {
            float along = Vector3.Dot(obstacle.rigidbody.velocity, direction.normalized);
            obstacle.rigidbody.AddForce(direction.normalized * Mathf.Clamp(pushSpeed - along, 0f, acceleration * dt), ForceMode.VelocityChange);
        }
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


