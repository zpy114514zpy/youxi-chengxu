using UnityEngine;

// Apply after locomotion/arm animation, before the full-body shadow is copied.
[DefaultExecutionOrder(150)]
public sealed class CrouchPose : MonoBehaviour
{
    readonly Transform[] thighs=new Transform[2], knees=new Transform[2], feet=new Transform[2];
    readonly Quaternion[] thighRotation=new Quaternion[2], kneeRotation=new Quaternion[2], footRotation=new Quaternion[2];
    Transform hips;
    Animator animator;
    PlayerController player;
    Vector3 savedHips;
    bool applied;
    float drop;

    void Start()
    {
        player=GetComponent<PlayerController>();animator=GetComponentInChildren<Animator>();
        var bones=animator.GetComponentsInChildren<Transform>();
        hips=System.Array.Find(bones,t=>t.name=="mixamorig:Hips");
        for(int i=0;i<2;i++)
        {
            string prefix=i==0?"mixamorig:Left":"mixamorig:Right";
            thighs[i]=System.Array.Find(bones,t=>t.name==prefix+"UpLeg");
            knees[i]=System.Array.Find(bones,t=>t.name==prefix+"Leg");
            feet[i]=System.Array.Find(bones,t=>t.name==prefix+"Foot");
        }
    }
    void Update()
    {
        if(!applied)return;
        hips.localPosition=savedHips;
        for(int i=0;i<2;i++)if(thighs[i]&&knees[i]&&feet[i])
        {thighs[i].localRotation=thighRotation[i];knees[i].localRotation=kneeRotation[i];feet[i].localRotation=footRotation[i];}
        applied=false;
    }
    void LateUpdate()
    {
        if(!hips||!player)return;
        drop=Mathf.MoveTowards(drop,player.CrouchDrop,Time.deltaTime*2f);
        if(drop<=.0001f){player.PlantFeet();return;}
        savedHips=hips.localPosition;
        var targets=new Vector3[2];var rotations=new Quaternion[2];
        for(int i=0;i<2;i++)if(thighs[i]&&knees[i]&&feet[i])
        {
            targets[i]=feet[i].position;rotations[i]=feet[i].rotation;
            thighRotation[i]=thighs[i].localRotation;kneeRotation[i]=knees[i].localRotation;footRotation[i]=feet[i].localRotation;
        }
        hips.position-=Vector3.up*drop;
        for(int i=0;i<2;i++)if(thighs[i]&&knees[i]&&feet[i])
        {
            var upper=thighs[i];var lower=knees[i];var foot=feet[i];
            float a=Vector3.Distance(upper.position,lower.position),b=Vector3.Distance(lower.position,foot.position);
            Vector3 axis=(targets[i]-upper.position).normalized;
            float distance=Mathf.Clamp(Vector3.Distance(upper.position,targets[i]),Mathf.Abs(a-b)+.001f,a+b-.001f);
            float along=(a*a-b*b+distance*distance)/(2f*distance);
            Vector3 bend=Vector3.ProjectOnPlane(animator.transform.forward,axis).normalized;
            Vector3 kneeTarget=upper.position+axis*along+bend*Mathf.Sqrt(Mathf.Max(0,a*a-along*along));
            upper.rotation=Quaternion.FromToRotation(lower.position-upper.position,kneeTarget-upper.position)*upper.rotation;
            lower.rotation=Quaternion.FromToRotation(foot.position-lower.position,targets[i]-lower.position)*lower.rotation;
            foot.rotation=rotations[i];
        }
        player.PlantFeet();applied=true;
    }
}
