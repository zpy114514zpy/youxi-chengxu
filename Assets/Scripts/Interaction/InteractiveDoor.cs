using UnityEngine;
public sealed class InteractiveDoor : MonoBehaviour
{
    public Vector3 hingeLocal;
    public float openAngle=95f, angularSpeed=100f;
    float angle,target;
    public bool IsOpen => Mathf.Abs(target)>5f || Mathf.Abs(angle)>5f;
    public bool IsOpening => Mathf.Abs(target)>5f;
    Vector3 hinge;
    // 这一次开门的动作有没有因为“撞到东西”改成往反方向开：
    // 门扇往里开会被房里的家具顶住时，自动改成往外开，免得玩家按了 E 门却纹丝不动。
    bool flipped;
    void Awake(){hinge=transform.TransformPoint(hingeLocal);}
    public void Toggle(Vector3 playerPosition)
    {
        if(Mathf.Abs(target)>.1f){target=0;flipped=false;return;}
        var r=GetComponentInChildren<Renderer>();
        Vector3 away=r.bounds.center-playerPosition;
        Vector3 tangent=Vector3.Cross(Vector3.up,r.bounds.center-hinge);
        target=Vector3.Dot(tangent,away)>=0?openAngle:-openAngle;
        flipped=false;
    }
    void FixedUpdate()
    {
        float next=Mathf.MoveTowards(angle,target,angularSpeed*Time.fixedDeltaTime);
        if(Mathf.Approximately(next,angle))return;
        Vector3 oldPosition=transform.position;Quaternion oldRotation=transform.rotation;
        transform.RotateAround(hinge,Vector3.up,next-angle);Physics.SyncTransforms();

        string blocker=null;
        foreach(var c in GetComponentsInChildren<Collider>())
        {
            if(!c.enabled||c.isTrigger)continue;
            foreach(var other in Physics.OverlapBox(c.bounds.center,c.bounds.extents,Quaternion.identity,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
            {
                if(other.transform.IsChildOf(transform))continue;
                // 玩家自己不算挡门：贴着门按 E 的时候，门扇一开始会扫到角色胶囊，
                // 结果门每次都“弹回去”，看起来就是这扇门永远打不开。
                if(other.GetComponentInParent<PlayerController>())continue;
                // 矮于 0.35m 的东西（地毯、门口的积木堆这类）不挡门：门扇直接从上面扫过去。
                // 否则门口一放小物件，门就会卡在半路开不动。
                if(other.bounds.size.y<.35f)continue;
                if(Physics.ComputePenetration(c,c.transform.position,c.transform.rotation,other,other.transform.position,other.transform.rotation,out var dir,out float depth)&&depth>.025f)
                { blocker=other.name;break; }
            }
            if(blocker!=null)break;
        }

        if(blocker==null){angle=next;return;}

        // 撞到了：先退回上一步，再决定是“换一边开”还是“老老实实停在这”。
        transform.SetPositionAndRotation(oldPosition,oldRotation);Physics.SyncTransforms();
        // 只有“几乎还没开起来”的时候才换方向：门已经开了大半再被顶住的话，
        // 直接停在这个角度就好，不然会像被人一把甩回去。
        if(!flipped&&Mathf.Abs(angle)<20f&&Mathf.Abs(target)>5f)
        {
            flipped=true;
            target=-target;
            Debug.Log("[门] "+name+" 往这边开会撞到“"+blocker+"”，改成往另一边开。",this);
        }
        else
        {
            target=angle;
            Debug.LogWarning("[门] "+name+" 被“"+blocker+"”挡住，开不动（已停下）。",this);
        }
    }
}
