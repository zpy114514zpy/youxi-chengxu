using UnityEngine;

// Place on an individual cabinet leaf or drawer, not on the entire cabinet.
public sealed class FurnitureOpenable : MonoBehaviour
{
    public bool slide;
    public Vector3 slideDirection = Vector3.forward;
    public float travel = .28f;
    public float openAngle = 75f;
    public float speed = 2f;
    Vector3 closedPosition;
    Quaternion closedRotation;
    float amount;
    bool opened;
    Vector3 openingDirection;
    public string Prompt => slide ? "E 打开 / 关闭抽屉" : "E 打开 / 关闭柜门";

    void Awake()
    {
        closedPosition = transform.position;
        closedRotation = transform.rotation;
        slideDirection = slideDirection.sqrMagnitude < .001f ? Vector3.forward : slideDirection.normalized;
        openingDirection=slideDirection;
    }

    public void Toggle(Vector3 playerPosition)
    {
        if(!opened && slide)
        {
            var visual=GetComponentInChildren<Renderer>();
            Vector3 center=visual?visual.bounds.center:transform.position;
            Vector3 towardPlayer=Vector3.ProjectOnPlane(playerPosition-center,Vector3.up);
            openingDirection=Vector3.Dot(slideDirection,towardPlayer)<0f?-slideDirection:slideDirection;
        }
        opened=!opened;
    }
    public void Toggle() { Toggle(transform.position+slideDirection); }

    void Update()
    {
        amount = Mathf.MoveTowards(amount, opened ? 1f : 0f, speed * Time.deltaTime);
        if (slide)
            transform.position = closedPosition + openingDirection * (travel * amount);
        else
            transform.SetPositionAndRotation(closedPosition, Quaternion.AngleAxis(openAngle * amount, Vector3.up) * closedRotation);
    }
}
