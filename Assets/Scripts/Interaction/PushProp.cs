using UnityEngine;
[RequireComponent(typeof(Rigidbody))]
[DefaultExecutionOrder(10)]
public sealed class PushProp : MonoBehaviour
{
    [Min(0f)] public float speed = 0.8f;
    Rigidbody body;
    Vector3 pendingMove;
    float fallSpeed;
    void Awake(){body=GetComponent<Rigidbody>();body.isKinematic=true;}
    public bool TryPush(Vector3 direction, float dt)
    {
        if(!body)body=GetComponent<Rigidbody>();
        direction=Vector3.ProjectOnPlane(direction,Vector3.up).normalized;
        float distance=speed*dt;
        foreach(var hit in body.SweepTestAll(direction,distance+.015f,QueryTriggerInteraction.Ignore))
            if(!hit.collider.GetComponentInParent<PlayerController>() && Vector3.Dot(hit.normal,direction)<-.1f)
                distance=Mathf.Min(distance,Mathf.Max(0,hit.distance-.015f));
        pendingMove=direction*distance;
        return distance>.001f;
    }
    void FixedUpdate()
    {
        // Kinematic horizontal control prevents pushing without Q; a downward sweep
        // still lets a box fall naturally when pushed off a shelf or ledge.
        fallSpeed+=9.81f*Time.fixedDeltaTime;
        float drop=fallSpeed*Time.fixedDeltaTime;
        foreach(var hit in body.SweepTestAll(Vector3.down,drop+.005f,QueryTriggerInteraction.Ignore))
            if(hit.normal.y>.1f){float allowed=Mathf.Max(0,hit.distance-.005f);if(allowed<drop){drop=allowed;fallSpeed=0;}}
        body.MovePosition(body.position+pendingMove+Vector3.down*drop);
        pendingMove=Vector3.zero;
    }
}
