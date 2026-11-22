using System.Collections.Generic;
using UnityEngine;
public sealed class BedroomToyPuzzle:SequencePlacementPuzzle
{
    public GameObject fragmentObject;
    readonly HashSet<int> placed = new HashSet<int>();
    bool solved;

    /// <summary>四个玩偶是不是都归位了。书桌抽屉的剧情锁、房门的剧情锁都要看它。</summary>
    public bool Solved => solved;
    void Awake()
    {
        var letterModel=GameObject.Find("1 (1)");
        var realFragment=letterModel?letterModel.transform.Find("对象007"):null;
        if(!realFragment)realFragment=DrawerFragmentReveal.FindDeep("对象007");
        if(!realFragment&&fragmentObject)realFragment=fragmentObject.transform;
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
        pickup.description="碎片一：以后我一定会好好爱护我的玩偶的！";
        var envelope=fragmentObject.GetComponent<EnvelopeFragment>();
        if(!envelope)envelope=fragmentObject.AddComponent<EnvelopeFragment>();
        envelope.fragmentName="信封碎片①";
        envelope.text="以后我一定会好好爱护我的玩偶的！";
        if(!fragmentObject.GetComponent<Collider>())
        {
            // 模型节点带缩放（书桌实例是 88.8 倍），碰撞体尺寸要按世界尺寸换算回局部空间。
            var renderer=fragmentObject.GetComponent<Renderer>();
            var bounds=renderer?renderer.bounds:new Bounds(fragmentObject.transform.position,Vector3.one*.1f);
            var scale=fragmentObject.transform.lossyScale;
            float sx=Mathf.Max(.0001f,Mathf.Abs(scale.x)),sy=Mathf.Max(.0001f,Mathf.Abs(scale.y)),sz=Mathf.Max(.0001f,Mathf.Abs(scale.z));
            if(fragmentObject.GetComponent<MeshFilter>())
            {
                var box=fragmentObject.AddComponent<BoxCollider>();
                box.center=fragmentObject.transform.InverseTransformPoint(bounds.center);
                box.size=new Vector3(
                    Mathf.Max(bounds.size.x/sx,.02f/sx),
                    Mathf.Max(bounds.size.y/sy,.02f/sy),
                    Mathf.Max(bounds.size.z/sz,.02f/sz));
            }
            else
            {
                // 碎片本身没有网格时，用子物体包围盒给一个够大的球体碰撞，保证能拾取。
                var renderers=fragmentObject.GetComponentsInChildren<Renderer>(true);
                Bounds combined=bounds;
                for(int i=0;i<renderers.Length;i++)combined.Encapsulate(renderers[i].bounds);
                var sphere=fragmentObject.AddComponent<SphereCollider>();
                sphere.center=fragmentObject.transform.InverseTransformPoint(combined.center);
                sphere.radius=Mathf.Max(.06f,combined.extents.magnitude/sx);
            }
        }
        fragmentObject.SetActive(false);
        // 谜题没解开之前，书桌中间的抽屉拉不开（左右两侧照常能开），
        // 叠在这里配一次，免得玩家提前把中间的抽屉翻个遍。
        if(!GetComponent<DeskDrawerGate>())gameObject.AddComponent<DeskDrawerGate>();
    }
    public override bool CanPlace(int order) => !solved && !placed.Contains(order);
    public override void OnPlaced(int order)
    {
        if(!placed.Add(order) || solved || placed.Count<requiredSlots)return;
        solved=true;
        if(revealWhenSolved!=null)foreach(var reward in revealWhenSolved)if(reward)reward.SetActive(true);
        StoryProgress.Instance?.RecordClue("四个玩偶都已回到床上，书桌抽屉里出现了一张信封碎片。");
        OnSolved();
    }
    protected override void OnSolved()
    {
        StoryProgress.Instance?.OnBedroomPuzzleSolved();
        if(!fragmentObject)return;
        // 碎片先不出现：等玩家把剧情 UI 跳过之后，由 DrawerFragmentReveal 拉开抽屉再把碎片放进去。
        fragmentObject.SetActive(false);
        var reveal=GetComponent<DrawerFragmentReveal>();
        if(!reveal)reveal=gameObject.AddComponent<DrawerFragmentReveal>();
        reveal.Begin(fragmentObject);
    }
}
