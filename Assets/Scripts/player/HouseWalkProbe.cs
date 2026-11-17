#if UNITY_EDITOR
using UnityEngine;
using System.Linq;
using System.Collections;
using System.Reflection;

[DefaultExecutionOrder(1000)]
class HouseWalkProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Attach(){var p=FindObjectOfType<PlayerController>();if(p)p.gameObject.AddComponent<HouseWalkProbe>();}
    PlayerController p;
    FieldInfo dir,run,jump;
    Vector3 start,travel;
    Mesh sample;
    int phase;
    int samples;
    float minGap,maxGap,peak,startY;
    void Awake(){
        p=GetComponent<PlayerController>();
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        dir=typeof(PlayerController).GetField("direction",flags);
        run=typeof(PlayerController).GetField("running",flags);
        jump=typeof(PlayerController).GetField("jumpRequested",flags);
        sample=new Mesh();
    }
    IEnumerator Start(){
        yield return new WaitForSeconds(0.6f);
        var cap=p.GetComponentInChildren<CapsuleCollider>();
        var origin=cap.bounds.center;
        float farthest=0;
        for(int i=0;i<24;i++){
            var v=Quaternion.Euler(0,i*15,0)*Vector3.forward;
            float d=Physics.SphereCast(origin,0.2f,v,out var h,4f)?h.distance:4f;
            if(d>farthest){farthest=d;travel=v;}
        }
        for(int i=1;i<=3;i++){
            start=p.transform.position;startY=start.y;
            minGap=100;maxGap=-100;samples=0;phase=i;peak=startY;
            if(i==3)jump.SetValue(p,true);
            yield return new WaitForSeconds(i==1?0.95f:i==2?0.32f:1.6f);
            Debug.Log($"HOUSEWALK phase={i} distance={Vector3.ProjectOnPlane(p.transform.position-start,Vector3.up).magnitude:F3} samples={samples} soleGap={minGap:F4}..{maxGap:F4} dy={p.transform.position.y-startY:F3} peak={peak-startY:F3}");
        }
        phase=0;
    }
    void Update(){if(phase!=0){dir.SetValue(p,phase==1?travel:phase==2?-travel:Vector3.zero);run.SetValue(p,phase==2);}}
    void LateUpdate(){
        if(phase==0)return;
        peak=Mathf.Max(peak,p.transform.position.y);
        var s=p.GetComponentInChildren<SkinnedMeshRenderer>();
        s.BakeMesh(sample);
        Vector3 sole=Vector3.positiveInfinity;
        foreach(var v in sample.vertices){var point=s.transform.TransformPoint(v);if(point.y<sole.y)sole=point;}
        if(Physics.Raycast(sole+Vector3.up*0.07f,Vector3.down,out var hit,2f) && hit.rigidbody!=GetComponent<Rigidbody>()){
            samples++;float gap=sole.y-hit.point.y;minGap=Mathf.Min(minGap,gap);maxGap=Mathf.Max(maxGap,gap);
        }
    }
    void OnDestroy(){Destroy(sample);}
}
#endif
