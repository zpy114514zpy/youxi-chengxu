using System.Collections.Generic;
using UnityEngine;
public sealed class BedroomToyPuzzle:SequencePlacementPuzzle
{
    public GameObject fragmentObject;
    readonly HashSet<int> placed = new HashSet<int>();
    bool solved;
    void Awake()
    {
        var letterModel=GameObject.Find("1 (1)");
        var realFragment=letterModel?letterModel.transform.Find("对象007"):null;
        if(!realFragment)
        {
            Debug.LogError("第一关信封碎片“对象007”未找到。",this);
            return;
        }
        if(fragmentObject && fragmentObject!=realFragment.gameObject)fragmentObject.SetActive(false);
        fragmentObject=realFragment.gameObject;
        var pickup=fragmentObject.GetComponent<PickupItem>();
        if(!pickup)pickup=fragmentObject.AddComponent<PickupItem>();
        pickup.displayName="信封碎片①";
        pickup.storyId="letter_fragment_1";
        pickup.itemType="信封碎片";
        pickup.description="童年写给未来的信：长大以后我要一直开心。";
        if(!fragmentObject.GetComponent<EnvelopeFragment>())fragmentObject.AddComponent<EnvelopeFragment>();
        if(!fragmentObject.GetComponent<Collider>())
        {
            var mesh=fragmentObject.GetComponent<MeshFilter>();
            if(mesh && mesh.sharedMesh)
            {
                var bounds=mesh.sharedMesh.bounds;
                var box=fragmentObject.AddComponent<BoxCollider>();
                box.center=bounds.center;
                box.size=new Vector3(Mathf.Max(bounds.size.x,.025f),Mathf.Max(bounds.size.y,.025f),Mathf.Max(bounds.size.z,.025f));
            }
        }
        fragmentObject.SetActive(false);
    }
    public override bool CanPlace(int order) => !solved && !placed.Contains(order);
    public override void OnPlaced(int order)
    {
        if(!placed.Add(order) || solved || placed.Count<requiredSlots)return;
        solved=true;
        if(revealWhenSolved!=null)foreach(var reward in revealWhenSolved)if(reward)reward.SetActive(true);
        StoryProgress.Instance?.RecordClue("四个玩偶都已回到床上，书桌上出现了一张信封碎片。");
        OnSolved();
    }
    protected override void OnSolved()
    {
        if(fragmentObject)fragmentObject.SetActive(true);
    }
}
