using UnityEngine;
public sealed class StoryPlacement : MonoBehaviour
{
    public string requiredItemId;
    public string displayName="道具";
    public string DisplayName => displayName!="道具"?displayName:requiredItemId=="rabbit"?"小兔玩偶":requiredItemId=="toy_one"?"玩具一":requiredItemId=="toy_two"?"玩具二":requiredItemId;
    public int order;
    public SequencePlacementPuzzle puzzle;
    public bool filled;
    public bool Place(PlayerInventory inventory)
    {
        if(filled||!puzzle||!puzzle.CanPlace(order))return false;
        var item=inventory.ConsumeSelected(requiredItemId);if(!item)return false;
        item.transform.SetPositionAndRotation(transform.position,transform.rotation);
        item.gameObject.SetActive(true);
        var rb=item.GetComponent<Rigidbody>();if(rb){rb.velocity=Vector3.zero;rb.isKinematic=true;}
        foreach(var c in item.GetComponentsInChildren<Collider>())c.enabled=false;
        filled=true;puzzle.OnPlaced(order);return true;
    }
    void OnGUI()
    {
        if(filled||!Camera.main)return;
        var player=FindObjectOfType<PlayerController>();
        if(!player||(player.transform.position-transform.position).sqrMagnitude>4f)return;
        Vector3 point=Camera.main.WorldToScreenPoint(transform.position+Vector3.up*.18f);
        if(point.z<=0)return;
        GUI.Box(new Rect(point.x-90,Screen.height-point.y-38,180,30),"归位 "+(order+1)+" · "+DisplayName);
    }
}
