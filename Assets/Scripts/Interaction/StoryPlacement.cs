using UnityEngine;
public sealed class StoryPlacement : MonoBehaviour
{
    public string requiredItemId;
    public string displayName="道具";
    public string DisplayName => displayName!="道具"?displayName:requiredItemId=="rabbit"?"小兔玩偶":requiredItemId=="toy_one"?"玩具一":requiredItemId=="toy_two"?"玩具二":requiredItemId;
    public int order;
    public SequencePlacementPuzzle puzzle;
    public bool filled;
    [Tooltip("模型正面与 Unity +Z 轴之间的水平角度。")]
    public float faceYawOffset;
    public bool Place(PlayerInventory inventory)
    {
        if(filled||!puzzle||!puzzle.CanPlace(order))return false;
        var item=inventory.ConsumeSelected(requiredItemId);if(!item)return false;
        var player=inventory.GetComponent<PlayerController>();
        Vector3 toward=player?player.transform.position-transform.position:transform.forward;
        toward.y=0f;
        Quaternion facing=toward.sqrMagnitude>.001f?Quaternion.LookRotation(toward.normalized,Vector3.up):transform.rotation;
        item.transform.SetPositionAndRotation(transform.position,facing*Quaternion.Euler(0f,faceYawOffset,0f));
        item.gameObject.SetActive(true);
        var highlight=item.GetComponent<ProximityOutline>();if(highlight)highlight.enabled=false;
        var visual=item.GetComponentInChildren<Renderer>();
        if(visual)item.transform.position+=Vector3.up*(transform.position.y+.018f-visual.bounds.min.y);
        var rb=item.GetComponent<Rigidbody>();if(rb){rb.velocity=Vector3.zero;rb.isKinematic=true;}
        foreach(var c in item.GetComponentsInChildren<Collider>())c.enabled=false;
        foreach(var r in GetComponentsInChildren<Renderer>())r.enabled=false;
        foreach(var c in GetComponentsInChildren<Collider>())c.enabled=false;
        filled=true;puzzle.OnPlaced(order);return true;
    }
    void OnGUI()
    {
        if(filled||!Camera.main)return;
        var player=FindObjectOfType<PlayerController>();
        if(!player||(player.transform.position-transform.position).sqrMagnitude>4f)return;
        StoryPlacement focused=null;float best=float.MaxValue;
        foreach(var hit in Physics.RaycastAll(Camera.main.transform.position,Camera.main.transform.forward,2f,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
        {
            var candidate=hit.collider.GetComponentInParent<StoryPlacement>();
            if(candidate && hit.distance<best){focused=candidate;best=hit.distance;}
        }
        if(focused!=this)return;
        Vector3 point=Camera.main.WorldToScreenPoint(transform.position+Vector3.up*.18f);
        if(point.z<=0)return;
        GUI.Box(new Rect(point.x-90,Screen.height-point.y-38,180,30),"归位 · "+DisplayName);
    }
}
