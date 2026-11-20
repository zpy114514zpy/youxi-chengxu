using UnityEngine;
public sealed class InteractiveDoor : MonoBehaviour
{
    public Vector3 hingeLocal;
    public float openAngle=95f, angularSpeed=100f;
    float angle,target;
    public bool IsOpen => Mathf.Abs(target)>5f || Mathf.Abs(angle)>5f;
    public bool IsOpening => Mathf.Abs(target)>5f;
    Vector3 hinge;
    void Awake(){hinge=transform.TransformPoint(hingeLocal);}
    public void Toggle(Vector3 playerPosition)
    {
        if(Mathf.Abs(target)>.1f){target=0;return;}
        var r=GetComponentInChildren<Renderer>();
        Vector3 away=r.bounds.center-playerPosition;
        Vector3 tangent=Vector3.Cross(Vector3.up,r.bounds.center-hinge);
        target=Vector3.Dot(tangent,away)>=0?openAngle:-openAngle;
    }
    void FixedUpdate()
    {
        float next=Mathf.MoveTowards(angle,target,angularSpeed*Time.fixedDeltaTime);
        if(Mathf.Approximately(next,angle))return;
        Vector3 oldPosition=transform.position;Quaternion oldRotation=transform.rotation;
        transform.RotateAround(hinge,Vector3.up,next-angle);Physics.SyncTransforms();
        foreach(var c in GetComponentsInChildren<Collider>())
        {
            if(!c.enabled||c.isTrigger)continue;
            foreach(var other in Physics.OverlapBox(c.bounds.center,c.bounds.extents,Quaternion.identity,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            {
                if(other.transform.IsChildOf(transform))continue;
                if(Physics.ComputePenetration(c,c.transform.position,c.transform.rotation,other,other.transform.position,other.transform.rotation,out var dir,out float depth)&&depth>.025f)
                { transform.SetPositionAndRotation(oldPosition,oldRotation);Physics.SyncTransforms();target=angle;return; }
            }
        }
        angle=next;
    }
}
