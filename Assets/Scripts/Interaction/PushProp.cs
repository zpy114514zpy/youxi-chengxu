using UnityEngine;
[RequireComponent(typeof(Rigidbody))]
[DefaultExecutionOrder(10)]
public sealed class PushProp : MonoBehaviour
{
    [Min(0f)] public float speed = 0.8f;
    [Tooltip("碰撞盒底面比实际物体底部高出多少（用来跨过门槛、小台阶）。贴地检测会补偿这个值，不会悬空。")]
    [Min(0f)] public float footLift;
    Rigidbody body;
    Vector3 pendingMove;
    float fallSpeed;
    void Awake(){body=GetComponent<Rigidbody>();body.isKinematic=true;}
    /// <summary>沿 direction 推这一帧。返回沿推动方向实际前进的距离（米），被门框/墙挡住就是 0。</summary>
    public float TryPush(Vector3 direction, float dt)
    {
        if(!body)body=GetComponent<Rigidbody>();
        direction=Vector3.ProjectOnPlane(direction,Vector3.up).normalized;
        float distance=speed*dt;
        float allowed=distance;
        Vector3 normal=Vector3.zero;
        foreach(var hit in body.SweepTestAll(direction,distance+.015f,QueryTriggerInteraction.Ignore))
        {
            if(hit.collider.GetComponentInParent<PlayerController>())continue;
            if(Vector3.Dot(hit.normal,direction)>=-.1f)continue;
            float limit=Mathf.Max(0,hit.distance-.015f);
            if(limit>=allowed)continue;
            allowed=limit;normal=hit.normal;
        }
        Vector3 move=direction*allowed;
        // 顶到门框/墙角时，把被挡下来的那段位移转成沿障碍面的滑行，
        // 否则稍微歪一点就整只箱子被门框顶死，怎么推都进不去。
        if(normal!=Vector3.zero && allowed<distance)
        {
            Vector3 slide=Vector3.ProjectOnPlane(direction*(distance-allowed),normal);
            slide=Vector3.ProjectOnPlane(slide,Vector3.up);
            move+=slide;
        }
        pendingMove=move;
        // 用实际位移长度（含沿门框的滑行），角色照这个速度跟上来才不会掉队；
        // 如果只取前进分量，箱子贴墙滑行时人会被算成“没动”，一停就再也推不动了。
        return move.magnitude;
    }
    void FixedUpdate()
    {
        if(!body)body=GetComponent<Rigidbody>();
        Vector3 move=pendingMove;
        pendingMove=Vector3.zero;
        move+=Vector3.down*FloorDrop();
        if(move.sqrMagnitude>.0000001f)body.MovePosition(body.position+move);
    }

    /// <summary>
    /// 这一帧该往下掉多少：贴地返回 0，嵌进地面返回负数（轻轻抬起）。
    /// 用射线量“碰撞盒底面到支撑面”的距离，而不是 SweepTest ——
    /// 盒子本来就贴着（或嵌进）地板时扫掠拿不到命中，会误判成悬空，然后一直往下沉。
    /// </summary>
    float FloorDrop()
    {
        Collider col=body?body.GetComponent<Collider>():GetComponent<Collider>();
        if(!col)return 0f;
        Bounds b=col.bounds;
        float support=float.NegativeInfinity;
        float ex=b.extents.x*.6f, ez=b.extents.z*.6f;
        for(int i=0;i<5;i++)
        {
            Vector3 p=b.center;
            if(i==1)p.x+=ex;else if(i==2)p.x-=ex;else if(i==3)p.z+=ez;else if(i==4)p.z-=ez;
            if(!Physics.Raycast(new Vector3(p.x,b.min.y+.05f,p.z),Vector3.down,out var hit,
                b.extents.y*2f+2f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))continue;
            if(hit.collider.transform.IsChildOf(transform))continue;
            if(hit.normal.y<.5f)continue;
            if(hit.point.y>support)support=hit.point.y;   // 取最高的那一层当支撑面
        }
        if(float.IsNegativeInfinity(support)){fallSpeed+=9.81f*Time.fixedDeltaTime;return fallSpeed*Time.fixedDeltaTime;}
        // 碰撞盒底面被有意抬高了 footLift，减掉它，让物体底部（而不是碰撞盒底部）贴住地面。
        float gap=b.min.y-footLift-support;
        if(gap<=.004f){fallSpeed=0f;return gap<-.004f?Mathf.Max(gap,-.02f):0f;}
        fallSpeed+=9.81f*Time.fixedDeltaTime;
        return Mathf.Min(fallSpeed*Time.fixedDeltaTime,gap);
    }
}
