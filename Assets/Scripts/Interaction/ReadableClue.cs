using UnityEngine;
public sealed class ReadableClue : MonoBehaviour
{
    [TextArea] public string title="线索";
    [TextArea(3,8)] public string description="";
    public float glowRange=1.8f;
    public Vector3 labelOffset=Vector3.up*.25f;
    [Tooltip("靠近时是否让物体自发光（墙上的日历这类大张贴物建议关掉）。")]
    public bool emissive=true;
    [TextArea] public string guideAfterRead;
    bool guideShown;
    Renderer[] meshes;MaterialPropertyBlock block;Camera view;Transform player;
    void Start()
    {
        meshes=GetComponentsInChildren<Renderer>();block=new MaterialPropertyBlock();view=Camera.main;
        var p=FindObjectOfType<PlayerController>();if(p)player=p.transform;
        if(title.Contains("绘本"))
        {
            if(!GetComponent<ProximityOutline>())gameObject.AddComponent<ProximityOutline>();
            return;
        }
        foreach(var r in meshes)if(r)foreach(var material in r.materials)if(material && material.HasProperty("_EmissionColor"))material.EnableKeyword("_EMISSION");
    }
    void LateUpdate()
    {
        if(!player||meshes==null||block==null)return;
        if(title.Contains("绘本")||!emissive)return;
        bool near=(player.position-transform.position).sqrMagnitude<glowRange*glowRange;
        foreach(var r in meshes)if(r)
        {r.GetPropertyBlock(block);block.SetColor("_EmissionColor",near?Color.white*1.8f:Color.black);r.SetPropertyBlock(block);}
    }
    public void Read()
    {
        string text=title.Contains("绘本")?"绘本里画着四个玩偶。找到它们，然后放到小床上对应的归位点。摆放顺序不限。":(description??"").Replace("照着绘本的顺序来","找到各自的归位点即可");
        StoryProgress.Instance?.Read(title,text);
        if (!guideShown && !string.IsNullOrEmpty(guideAfterRead))
        {
            guideShown=true;
            StoryProgress.Instance?.Enqueue(title.Contains("绘本")
                ?"先找到四个玩偶，再把它们放到床上对应的归位点。可以按任意顺序摆放。"
                :guideAfterRead);
        }
    }
    void OnGUI()
    {
        if(!player||!view||StoryProgress.Instance && StoryProgress.Instance.IsReading ||
           (player.position-transform.position).sqrMagnitude>glowRange*glowRange)return;
        ReadableClue focused=null;float best=float.MaxValue;
        foreach(var hit in Physics.RaycastAll(view.transform.position,view.transform.forward,glowRange,Physics.DefaultRaycastLayers,QueryTriggerInteraction.Ignore))
        {
            var clue=hit.collider.GetComponentInParent<ReadableClue>();
            if(clue && hit.distance<best){focused=clue;best=hit.distance;}
        }
        if(focused!=this)return;
        Vector3 top=transform.position+labelOffset;
        var r=GetComponentInChildren<Renderer>();if(r)top=new Vector3(r.bounds.center.x,r.bounds.max.y+.12f,r.bounds.center.z);
        var screen=view.WorldToScreenPoint(top);if(screen.z<=0)return;
        var style=new GUIStyle(GUI.skin.box){fontSize=15,alignment=TextAnchor.MiddleCenter,wordWrap=true};
        GUI.Box(new Rect(screen.x-150,Screen.height-screen.y-48,300,38),"E 阅读 · "+title,style);
    }
    void OnDisable(){if(meshes!=null)foreach(var r in meshes)if(r)r.SetPropertyBlock(null);}
}
