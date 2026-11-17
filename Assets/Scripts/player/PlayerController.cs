using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [Header("移动")]
    [Min(0f)] public float walkSpeed = 2f;
    [Min(0f)] public float moveSpeed = 7f;
    [Min(0f)] public float groundDrag = 7f;
    public float jumpForce = 12f;
    public float jumpCooldown = 0.25f;
    public float airMultiplier = 0.4f;
    [Min(0f)] public float gravityMultiplier = 2f;
    public KeyCode jumpKey = KeyCode.Space;
    public float playerHeight = 2f;
    public LayerMask Ground;
    public Transform orientation;

    [Header("角色动作")]
    public KeyCode runKey = KeyCode.LeftShift;
    public KeyCode pickupKey = KeyCode.F;
    public KeyCode pushKey = KeyCode.E;
    [Min(0f)] public float pushSpeed = 1f;
    [Min(0f)] public float pushReach = 0.25f;

    static readonly int Idle = State("Idle"), Walk = State("Walk"), Run = State("Run"),
        Pickup = State("Pickup"), Push = State("Push"), Jump = State("Jump");
    Rigidbody body;
    Animator animator;
    CapsuleCollider capsule;
    ShoeGrounding shoes;
    bool onFloor;
    Vector3 direction;
    bool running, pushHeld, jumping, jumpRequested;
    float nextJumpTime, pickupUntil, pickupDuration;
    int playing;

    [Header("游戏视角")]
    public bool followPlayer = true;
    Camera viewCamera;
    Vector3 cameraOffset;

    void Start()
    {
        shoes = new ShoeGrounding(animator, capsule, FloorMask);
        viewCamera = Camera.main;
        if (!viewCamera || !followPlayer) return;
        cameraOffset = Quaternion.Euler(0f, animator.transform.eulerAngles.y, 0f) * new Vector3(1.1f, 0.38f, -1.65f);
        viewCamera.nearClipPlane = 0.05f;
        LateUpdate();
    }

    void LateUpdate()
    {
        if (onFloor && !jumping) shoes?.Plant();
        if (!followPlayer || !viewCamera) return;
        Vector3 target = capsule.bounds.center;
        Vector3 directionToCamera = cameraOffset.normalized;
        float distance = cameraOffset.magnitude;
        // 从角色碰撞体外向后检查，避免相机落在房间墙外。
        foreach (RaycastHit hit in Physics.SphereCastAll(target, 0.08f, directionToCamera,
            distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            if (hit.collider.attachedRigidbody != body)
                distance = Mathf.Min(distance, Mathf.Max(0.15f, hit.distance - 0.03f));
        viewCamera.transform.SetPositionAndRotation(target + directionToCamera * distance,
            Quaternion.LookRotation(-directionToCamera, Vector3.up));
    }

    static int State(string name) => Animator.StringToHash("Base Layer." + name);

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        animator = GetComponentInChildren<Animator>();
        capsule = GetComponentInChildren<CapsuleCollider>();
        body.freezeRotation = true;
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
        running = Input.GetKey(runKey);
        pushHeld = Input.GetKey(pushKey);
        if (!jumping && !pushHeld && Time.time >= pickupUntil && Input.GetKeyDown(pickupKey))
        {
            pickupUntil = Time.time + pickupDuration;
            Play(Pickup);
        }
        if (Input.GetKeyDown(jumpKey)) jumpRequested = true;
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
        if (jumping && grounded && body.velocity.y <= 0.05f) jumping = false;
        bool taking = Time.time < pickupUntil;
        if (jumpRequested && grounded && !taking && Time.time >= nextJumpTime)
        {
            jumping = true;
            nextJumpTime = Time.time + jumpCooldown;
            body.velocity = new Vector3(body.velocity.x, jumpForce, body.velocity.z);
        }
        jumpRequested = false;

        bool moving = !taking && direction.sqrMagnitude > 0.001f;
        RaycastHit obstacle = default;
        bool pushing = moving && pushHeld && grounded && !jumping &&
            body.SweepTest(direction.normalized, out obstacle, pushReach, QueryTriggerInteraction.Ignore)
            && Mathf.Abs(obstacle.normal.y) < 0.65f;
        float speed = pushing ? pushSpeed : running ? moveSpeed : walkSpeed;
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
        if (moving)
            animator.transform.rotation = Quaternion.RotateTowards(animator.transform.rotation,
                Quaternion.LookRotation(direction), 720f * dt);
        Play(taking ? Pickup : jumping ? Jump : pushing ? Push : moving ? (running ? Run : Walk) : Idle);
    }

    void Play(int state)
    {
        if (playing == state) return;
        animator.CrossFadeInFixedTime(state, 0.1f, 0, 0f);
        playing = state;
    }
}
